namespace VirnectMonitor.Auth;

public sealed class AuthOptions
{
    public string PublicBaseUrl { get; set; } = "";

    public string DatabasePath { get; set; } = "Data/auth-v2.db";

    public string ServerSecret { get; set; } = "development-only-change-me";

    public int LoginExpiresMinutes { get; set; } = 10;

    public int AuthDurationMinutes { get; set; } = 30;

    public int MaxFailureCount { get; set; } = 5;

    public bool EnableLatestApprovedCompatMode { get; set; } = true;

    public int SessionCleanupIntervalSeconds { get; set; } = 30;

    public int ExpiredSessionRetentionMinutes { get; set; } = 10;
}

