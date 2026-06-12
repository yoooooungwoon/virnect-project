namespace VirnectMonitor.Auth;

public sealed class UserAccount
{
    public long Id { get; set; }

    public required string Username { get; set; }

    public required string UsernameNormalized { get; set; }

    public required string PasswordHash { get; set; }

    public required string Role { get; set; }

    public required string Status { get; set; }

    public long CreatedAt { get; set; }

    public long? LastLoginAt { get; set; }
}

public static class UserRoles
{
    public const string Admin = "admin";
    public const string Viewer = "viewer";
}

public static class UserStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}

