namespace VirnectMonitor.Auth;

public sealed class LoginAuditEvent
{
    public long Id { get; set; }

    public string? Username { get; set; }

    public string? UsernameNormalized { get; set; }

    public required string Result { get; set; }

    public string? Reason { get; set; }

    public long? SessionId { get; set; }

    public string? ClientIp { get; set; }

    public string? UserAgent { get; set; }

    public long OccurredAt { get; set; }
}

public static class LoginAuditResults
{
    public const string Success = "success";
    public const string Failure = "failure";
}

