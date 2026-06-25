namespace VirnectMonitor.Models;

public sealed class DiscordOptions
{
    public bool Enabled { get; set; }

    public string Username { get; set; } = "VIRNECT Monitor";

    public string? AvatarUrl { get; set; }

    public string? WebhookFilePath { get; set; }

    public Dictionary<string, string> ServerWebhooks { get; set; } = new();
}
