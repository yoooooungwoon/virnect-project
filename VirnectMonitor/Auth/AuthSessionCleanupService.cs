using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class AuthSessionCleanupService : BackgroundService
{
    private readonly AuthSessionRepository _sessions;
    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthSessionCleanupService> _logger;

    public AuthSessionCleanupService(
        AuthSessionRepository sessions,
        IOptionsMonitor<AuthOptions> options,
        TimeProvider timeProvider,
        ILogger<AuthSessionCleanupService> logger)
    {
        _sessions = sessions;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(5, _options.CurrentValue.SessionCleanupIntervalSeconds));
            await Task.Delay(delay, stoppingToken);
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            var retentionSeconds = Math.Max(0, _options.CurrentValue.ExpiredSessionRetentionMinutes) * 60L;
            var expired = await _sessions.ExpireStaleSessionsAsync(now);
            var deleted = await _sessions.DeleteOldExpiredSessionsAsync(now - retentionSeconds);

            if (expired > 0 || deleted > 0)
            {
                _logger.LogInformation(
                    "Auth session cleanup completed. Expired={ExpiredCount}, DeletedExpiredSessions={DeletedCount}",
                    expired,
                    deleted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auth session cleanup failed.");
        }
    }
}

