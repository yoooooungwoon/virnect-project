namespace VirnectMonitor.Auth;

public sealed record StartAuthResponse(
    string Token,
    string LoginUrl,
    string Status,
    int Value,
    DateTimeOffset LoginExpiresAt);

public sealed record LoginRequest(
    string Token,
    string Username,
    string Password);

public sealed record LogoutRequest(
    string? Token,
    string Command);

public sealed record LogoutResponse(
    string Status,
    int Value,
    bool LoggedOut);

public sealed record LoginAttemptMetadata(
    string? ClientIp,
    string? UserAgent);

public sealed record SetupAdminRequest(
    string Username,
    string Password,
    string ConfirmPassword);

public sealed record SetupStatusResponse(
    bool SetupRequired,
    string? Username = null,
    string? Role = null);

public sealed record AuthStatusResponse(
    string Status,
    int Value,
    bool Approved,
    bool Authenticated,
    bool TransitionConsumed,
    DateTimeOffset? LoginExpiresAt = null,
    DateTimeOffset? AuthExpiresAt = null,
    string? Username = null);

public sealed record AuthSessionView(
    long Id,
    string Status,
    bool TransitionConsumed,
    string? Username,
    int FailureCount,
    string? ClientSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset LoginExpiresAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? AuthExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastCheckedAt);

public sealed record LoginAuditView(
    long Id,
    string? Username,
    string Result,
    string? Reason,
    long? SessionId,
    string? ClientIp,
    string? UserAgent,
    DateTimeOffset OccurredAt);

