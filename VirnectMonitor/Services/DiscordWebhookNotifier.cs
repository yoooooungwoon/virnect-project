using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

public sealed partial class DiscordWebhookNotifier(
    HttpClient http,
    IOptionsMonitor<DiscordOptions> options,
    ILogger<DiscordWebhookNotifier> logger)
{
    public async Task SendLevelChangedAsync(
        string server,
        MetricSpec spec,
        double value,
        Level level,
        Level previousLevel,
        long createdAt,
        CancellationToken ct)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
            return;

        var webhookUrl = ResolveWebhookUrl(currentOptions, server);
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        var payload = BuildPayload(currentOptions, server, spec, value, level, previousLevel, createdAt);

        try
        {
            using var response = await http.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Discord 알림 전송 실패: {Server}/{Metric} HTTP {StatusCode}",
                    server,
                    spec.Id,
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discord 알림 전송 실패: {Server}/{Metric}", server, spec.Id);
        }
    }

    private static object BuildPayload(
        DiscordOptions options,
        string server,
        MetricSpec spec,
        double value,
        Level level,
        Level previousLevel,
        long createdAt)
    {
        var statusLabel = AlertStatusLabel(level);
        var prefix = AlertPrefix(level);
        var description = level == Level.Clean
            ? $"{spec.Name}이 정상 상태로 복구되었습니다."
            : $"{spec.Name}이 {statusLabel} 상태입니다.";

        var payload = new Dictionary<string, object?>
        {
            ["username"] = string.IsNullOrWhiteSpace(options.Username) ? "VIRNECT Monitor" : options.Username,
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
            ["embeds"] = new[]
            {
                new
                {
                    title = $"{prefix} {server}",
                    description,
                    color = AlertColor(level),
                    fields = new[]
                    {
                        new { name = "서버", value = server, inline = true },
                        new { name = "지표", value = spec.Name, inline = true },
                        new { name = "현재값", value = Metrics.Human(spec, value), inline = true },
                        new { name = "상태변경", value = $"{AlertStatusLabel(previousLevel)} -> {statusLabel}", inline = false },
                        new { name = "시간", value = $"{FormatKst(createdAt)} KST", inline = false },
                    },
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(createdAt).ToUniversalTime().ToString("O"),
                    footer = new { text = "VIRNECT Monitor" },
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(options.AvatarUrl))
            payload["avatar_url"] = options.AvatarUrl;

        return payload;
    }

    private static string? ResolveWebhookUrl(DiscordOptions options, string server)
    {
        if (TryResolveFromMap(options.ServerWebhooks, server, out var configuredUrl))
            return configuredUrl;

        var fileMap = LoadWebhookFile(options.WebhookFilePath);
        return TryResolveFromMap(fileMap, server, out var fileUrl) ? fileUrl : null;
    }

    private static bool TryResolveFromMap(
        IReadOnlyDictionary<string, string>? map,
        string server,
        out string? webhookUrl)
    {
        webhookUrl = null;
        if (map is null || map.Count == 0)
            return false;

        foreach (var (key, value) in map)
        {
            if (!string.Equals(key, server, StringComparison.OrdinalIgnoreCase))
                continue;

            webhookUrl = NormalizeWebhookUrl(value);
            return webhookUrl is not null;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> LoadWebhookFile(string? path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return map;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var match = WebhookLineRegex().Match(line);
                if (!match.Success)
                    continue;

                var url = NormalizeWebhookUrl(match.Groups["url"].Value);
                if (url is not null)
                    map[match.Groups["server"].Value] = url;
            }
        }
        catch (IOException)
        {
            return map;
        }
        catch (UnauthorizedAccessException)
        {
            return map;
        }

        return map;
    }

    private static string? NormalizeWebhookUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }

    private static string AlertPrefix(Level level) => level switch
    {
        Level.Danger => "🔴 [위험 발생]",
        Level.Warning => "🟡 [경고 발생]",
        _ => "🟢 [정상 복구]",
    };

    private static string AlertStatusLabel(Level level) => level switch
    {
        Level.Danger => "위험",
        Level.Warning => "경고",
        _ => "정상",
    };

    private static int AlertColor(Level level) => level switch
    {
        Level.Danger => 0xFF3928,
        Level.Warning => 0xF59F00,
        _ => 0x2FB344,
    };

    private static string FormatKst(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            .ToOffset(TimeSpan.FromHours(9))
            .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\s*(?<server>server-\d+)\s*:\s*(?<url>https://\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WebhookLineRegex();
}
