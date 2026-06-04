namespace VirnectMonitor.Auth;

public sealed class AuthSession
{
    public long Id { get; set; }

    public required string TokenHash { get; set; }

    public required string Status { get; set; }

    public bool TransitionConsumed { get; set; }

    public string? Username { get; set; }

    public int FailureCount { get; set; }

    public string? ClientSource { get; set; }

    public long CreatedAt { get; set; }

    public long LoginExpiresAt { get; set; }

    public long? ApprovedAt { get; set; }

    public long? AuthExpiresAt { get; set; }

    public long? ConsumedAt { get; set; }

    public long? FailedAt { get; set; }

    public long? RevokedAt { get; set; }

    public long? LastCheckedAt { get; set; }
}

