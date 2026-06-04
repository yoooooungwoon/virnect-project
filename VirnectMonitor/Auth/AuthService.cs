using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class AuthService
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);

    private readonly AuthSessionRepository _sessions;
    private readonly UserRepository _users;
    private readonly LoginAuditRepository _audits;
    private readonly TokenService _tokens;
    private readonly PasswordHasher _passwords;
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        AuthSessionRepository sessions,
        UserRepository users,
        LoginAuditRepository audits,
        TokenService tokens,
        PasswordHasher passwords,
        IOptions<AuthOptions> options,
        TimeProvider timeProvider)
    {
        _sessions = sessions;
        _users = users;
        _audits = audits;
        _tokens = tokens;
        _passwords = passwords;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<bool> IsSetupRequiredAsync()
    {
        return !await _users.HasAnyUserAsync();
    }

    public async Task<SetupStatusResponse> GetSetupStatusAsync()
    {
        return new SetupStatusResponse(SetupRequired: await IsSetupRequiredAsync());
    }

    public async Task<SetupStatusResponse> CreateInitialAdminAsync(SetupAdminRequest request)
    {
        var username = request.Username?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Password confirmation does not match.");
        }

        var now = NowSeconds();
        var user = await _users.CreateFirstAdminAsync(username, _passwords.HashPassword(request.Password), now);
        return new SetupStatusResponse(SetupRequired: false, user.Username, user.Role);
    }

    public async Task<StartAuthResponse> StartAsync(HttpRequest request)
    {
        var now = NowSeconds();
        var loginExpiresAt = now + MinutesToSeconds(_options.LoginExpiresMinutes);
        var token = _tokens.CreateToken();
        var tokenHash = _tokens.HashToken(token);
        var clientSource = request.Headers.UserAgent.ToString();

        await _sessions.CreatePendingSessionAsync(tokenHash, clientSource, now, loginExpiresAt);

        var loginUrl = $"{GetPublicBaseUrl(request)}/login?token={Uri.EscapeDataString(token)}";
        return new StartAuthResponse(
            token,
            loginUrl,
            AuthStatuses.Pending,
            0,
            ToDateTimeOffset(loginExpiresAt));
    }

    public async Task<AuthStatusResponse> LoginAsync(LoginRequest request, LoginAttemptMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            await RecordLoginAuditAsync(request.Username, LoginAuditResults.Failure, "missing_token", null, metadata, NowSeconds());
            return MissingTokenResponse();
        }

        var now = NowSeconds();
        var tokenHash = _tokens.HashToken(request.Token);
        var session = await _sessions.GetByTokenHashAsync(tokenHash);
        if (session is null)
        {
            await RecordLoginAuditAsync(request.Username, LoginAuditResults.Failure, "session_not_found", null, metadata, now);
            return NotFoundResponse();
        }

        if (IsExpired(session, now))
        {
            await _sessions.MarkExpiredAsync(session.Id, now);
            session.Status = AuthStatuses.Expired;
            await RecordLoginAuditAsync(request.Username, LoginAuditResults.Failure, "session_expired", session.Id, metadata, now);
            return ToResponse(session, value: -2, authenticated: false);
        }

        if (session.Status == AuthStatuses.Approved)
        {
            await RecordLoginAuditAsync(request.Username, LoginAuditResults.Success, "already_approved", session.Id, metadata, now);
            return ToResponse(
                session,
                value: IsAuthenticated(session, now) ? 1 : 0,
                authenticated: IsAuthenticated(session, now));
        }

        if (session.Status is AuthStatuses.Failed or AuthStatuses.Revoked or AuthStatuses.Expired)
        {
            await RecordLoginAuditAsync(request.Username, LoginAuditResults.Failure, session.Status, session.Id, metadata, now);
            return ToResponse(session, ValueForStatus(session.Status), authenticated: false);
        }

        var user = await _users.GetByUsernameAsync(request.Username);
        var validCredential = user is not null
            && user.Status == UserStatuses.Active
            && _passwords.VerifyPassword(request.Password, user.PasswordHash);

        if (!validCredential)
        {
            var failureCount = session.FailureCount + 1;
            var lockSession = failureCount >= _options.MaxFailureCount;
            await _sessions.RecordFailureAsync(session.Id, now, lockSession);

            session.FailureCount = failureCount;
            session.LastCheckedAt = now;
            if (lockSession)
            {
                session.Status = AuthStatuses.Failed;
                session.FailedAt = now;
            }

            await RecordLoginAuditAsync(
                request.Username,
                LoginAuditResults.Failure,
                lockSession ? "invalid_credentials_locked" : "invalid_credentials",
                session.Id,
                metadata,
                now);

            return ToResponse(session, value: -1, authenticated: false);
        }

        var authExpiresAt = now + MinutesToSeconds(_options.AuthDurationMinutes);
        await _sessions.RevokeActiveSessionsForUserAsync(user!.Username, session.Id, now);
        await _sessions.ApproveAsync(session.Id, user.Username, now, authExpiresAt);
        await _users.UpdateLastLoginAsync(user.Id, now);

        session.Status = AuthStatuses.Approved;
        session.Username = user.Username;
        session.TransitionConsumed = false;
        session.ApprovedAt = now;
        session.AuthExpiresAt = authExpiresAt;
        session.LastCheckedAt = now;

        await RecordLoginAuditAsync(user.Username, LoginAuditResults.Success, "authenticated", session.Id, metadata, now);

        return ToResponse(session, value: 1, authenticated: true);
    }

    public async Task<AuthStatusResponse> CurrentAsync(string? token)
    {
        var now = NowSeconds();
        var session = await FindSessionAsync(token);

        if (session is null)
        {
            return NotFoundResponse();
        }

        if (IsExpired(session, now))
        {
            await _sessions.MarkExpiredAsync(session.Id, now);
            session.Status = AuthStatuses.Expired;
            session.LastCheckedAt = now;
            return ToResponse(session, value: -2, authenticated: false);
        }

        return ToResponse(
            session,
            ValueForStatus(session.Status),
            authenticated: IsAuthenticated(session, now));
    }

    public async Task<bool> CanUseLoginTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var now = NowSeconds();
        var session = await FindSessionAsync(token);
        if (session is null)
        {
            return false;
        }

        if (IsExpired(session, now))
        {
            await _sessions.MarkExpiredAsync(session.Id, now);
            return false;
        }

        return session.Status is AuthStatuses.Pending or AuthStatuses.Approved;
    }

    public async Task<AuthStatusResponse> CurrentOnceAsync(string? token)
    {
        var now = NowSeconds();
        var session = await FindCurrentSessionAsync(token);

        if (session is null)
        {
            return MakeViewInactiveResponse(AuthStatuses.NotFound);
        }

        if (IsExpired(session, now))
        {
            await _sessions.MarkExpiredAsync(session.Id, now);
            session.Status = AuthStatuses.Expired;
            session.LastCheckedAt = now;
            return ToResponse(session, value: 0, authenticated: false);
        }

        var authenticated = IsAuthenticated(session, now);

        return new AuthStatusResponse(
            session.Status,
            authenticated ? 1 : 0,
            Approved: authenticated,
            Authenticated: authenticated,
            TransitionConsumed: session.TransitionConsumed,
            LoginExpiresAt: ToDateTimeOffset(session.LoginExpiresAt),
            AuthExpiresAt: ToDateTimeOffset(session.AuthExpiresAt),
            Username: session.Username);
    }

    public async Task<IReadOnlyList<AuthSessionView>> ListSessionsAsync(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var sessions = await _sessions.ListRecentAsync(boundedLimit);
        return sessions.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<LoginAuditView>> ListLoginAuditsAsync(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var events = await _audits.ListRecentAsync(boundedLimit);
        return events.Select(ToView).ToList();
    }

    private async Task<AuthSession?> FindSessionAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return await _sessions.GetByTokenHashAsync(_tokens.HashToken(token));
    }

    private async Task<AuthSession?> FindCurrentSessionAsync(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            return await _sessions.GetByTokenHashAsync(_tokens.HashToken(token));
        }

        return _options.EnableLatestApprovedCompatMode
            ? await _sessions.GetLatestApprovedAsync()
            : null;
    }

    private async Task RecordLoginAuditAsync(
        string? username,
        string result,
        string reason,
        long? sessionId,
        LoginAttemptMetadata metadata,
        long now)
    {
        await _audits.RecordAsync(
            username,
            result,
            reason,
            sessionId,
            metadata.ClientIp,
            metadata.UserAgent,
            now);
    }

    private string GetPublicBaseUrl(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/');
        }

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        return $"{scheme}://{host}".TrimEnd('/');
    }

    private long NowSeconds()
    {
        return _timeProvider.GetUtcNow().ToUnixTimeSeconds();
    }

    private static long MinutesToSeconds(int minutes)
    {
        return checked(minutes * 60L);
    }

    private static bool IsExpired(AuthSession session, long now)
    {
        return session.Status switch
        {
            AuthStatuses.Pending => session.LoginExpiresAt <= now,
            AuthStatuses.Approved => session.AuthExpiresAt is null || session.AuthExpiresAt <= now,
            _ => false
        };
    }

    private static bool IsAuthenticated(AuthSession session, long now)
    {
        return session.Status == AuthStatuses.Approved
            && session.AuthExpiresAt is not null
            && session.AuthExpiresAt > now;
    }

    private static int ValueForStatus(string status)
    {
        return status switch
        {
            AuthStatuses.Approved => 1,
            AuthStatuses.Pending => 0,
            AuthStatuses.Consumed => 0,
            AuthStatuses.Failed => -1,
            AuthStatuses.Expired => -2,
            AuthStatuses.NotFound => -3,
            _ => 0
        };
    }

    private static AuthStatusResponse ToResponse(AuthSession session, int value, bool authenticated)
    {
        return new AuthStatusResponse(
            session.Status,
            value,
            Approved: session.Status == AuthStatuses.Approved && value > 0,
            Authenticated: authenticated,
            TransitionConsumed: session.TransitionConsumed,
            LoginExpiresAt: ToDateTimeOffset(session.LoginExpiresAt),
            AuthExpiresAt: ToDateTimeOffset(session.AuthExpiresAt),
            Username: session.Username);
    }

    private static AuthStatusResponse MissingTokenResponse()
    {
        return new AuthStatusResponse(
            AuthStatuses.NotFound,
            -3,
            Approved: false,
            Authenticated: false,
            TransitionConsumed: false);
    }

    private static AuthStatusResponse NotFoundResponse()
    {
        return new AuthStatusResponse(
            AuthStatuses.NotFound,
            -3,
            Approved: false,
            Authenticated: false,
            TransitionConsumed: false);
    }

    private static AuthStatusResponse MakeViewInactiveResponse(string status)
    {
        return new AuthStatusResponse(
            status,
            0,
            Approved: false,
            Authenticated: false,
            TransitionConsumed: false);
    }

    private static AuthSessionView ToView(AuthSession session)
    {
        return new AuthSessionView(
            session.Id,
            session.Status,
            session.TransitionConsumed,
            session.Username,
            session.FailureCount,
            session.ClientSource,
            ToDateTimeOffset(session.CreatedAt),
            ToDateTimeOffset(session.LoginExpiresAt),
            ToDateTimeOffset(session.ApprovedAt),
            ToDateTimeOffset(session.AuthExpiresAt),
            ToDateTimeOffset(session.ConsumedAt),
            ToDateTimeOffset(session.FailedAt),
            ToDateTimeOffset(session.RevokedAt),
            ToDateTimeOffset(session.LastCheckedAt));
    }

    private static LoginAuditView ToView(LoginAuditEvent audit)
    {
        return new LoginAuditView(
            audit.Id,
            audit.Username,
            audit.Result,
            audit.Reason,
            audit.SessionId,
            audit.ClientIp,
            audit.UserAgent,
            ToDateTimeOffset(audit.OccurredAt));
    }

    private static DateTimeOffset ToDateTimeOffset(long seconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(KoreaOffset);
    }

    private static DateTimeOffset? ToDateTimeOffset(long? seconds)
    {
        return seconds is null ? null : DateTimeOffset.FromUnixTimeSeconds(seconds.Value).ToOffset(KoreaOffset);
    }
}

