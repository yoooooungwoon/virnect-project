// Prometheus HTTP API(instant query + range query) 호출과 결과 파싱
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

/// <summary>Prometheus 쿼리 결과 1건 (라벨 + 단일 값).</summary>
public readonly record struct InstantSample(IReadOnlyDictionary<string, string> Labels, double Value);

/// <summary>Prometheus 시계열 1건 (라벨 + 시간별 값 배열).</summary>
public readonly record struct RangeSeries(IReadOnlyDictionary<string, string> Labels, IReadOnlyList<(long Ts, double Value)> Points);

public sealed class PrometheusClient(IHttpClientFactory factory, IOptions<MonitorOptions> options)
{
    private readonly string _base = options.Value.PrometheusUrl.TrimEnd('/');

    /// <summary>현재값 1개를 뽑는 instant query (`/api/v1/query`).</summary>
    public async Task<IReadOnlyList<InstantSample>> QueryAsync(string promql, CancellationToken ct)
    {
        var url = $"{_base}/api/v1/query?query={Uri.EscapeDataString(promql)}";
        using var doc = await GetJsonAsync(url, ct);
        var result = new List<InstantSample>();
        foreach (var item in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        {
            var labels = ReadLabels(item);
            // value: [unixTs, "문자열 숫자"]
            if (TryParseValue(item.GetProperty("value")[1].GetString(), out var v))
                result.Add(new InstantSample(labels, v));
        }
        return result;
    }

    /// <summary>기간 추이를 뽑는 range query (`/api/v1/query_range`).</summary>
    public async Task<IReadOnlyList<RangeSeries>> QueryRangeAsync(
        string promql, long startUnix, long endUnix, int stepSeconds, CancellationToken ct)
    {
        var url = $"{_base}/api/v1/query_range?query={Uri.EscapeDataString(promql)}" +
                  $"&start={startUnix}&end={endUnix}&step={stepSeconds}";
        using var doc = await GetJsonAsync(url, ct);
        var result = new List<RangeSeries>();
        foreach (var item in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        {
            var labels = ReadLabels(item);
            var points = new List<(long, double)>();
            foreach (var pair in item.GetProperty("values").EnumerateArray())
            {
                var ts = (long)pair[0].GetDouble();
                if (TryParseValue(pair[1].GetString(), out var v))
                    points.Add((ts, v));
            }
            result.Add(new RangeSeries(labels, points));
        }
        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var http = factory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.GetProperty("status").GetString() != "success")
        {
            doc.Dispose();
            throw new InvalidOperationException("Prometheus 응답이 success가 아님");
        }
        return doc;
    }

    private static IReadOnlyDictionary<string, string> ReadLabels(JsonElement item)
    {
        var labels = new Dictionary<string, string>();
        foreach (var p in item.GetProperty("metric").EnumerateObject())
            labels[p.Name] = p.Value.GetString() ?? "";
        return labels;
    }

    private static bool TryParseValue(string? raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
