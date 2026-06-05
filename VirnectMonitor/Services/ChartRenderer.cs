// 시계열 데이터를 차트 이미지(SVG/PNG)로 렌더링 — AR이 이미지로 띄울 수 있게
using System.Globalization;
using System.Text;
using SkiaSharp;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

public static class ChartRenderer
{
    private const int PadL = 46, PadR = 12, PadT = 14, PadB = 22;

    private static (double X0, double X1, double Y0, double Y1) Bounds(
        IReadOnlyList<(long Ts, double Value)> pts, MetricSpec spec)
    {
        double x0 = pts[0].Ts, x1 = pts[^1].Ts;
        double maxV = double.MinValue;
        foreach (var p in pts) maxV = Math.Max(maxV, p.Value);
        double y0 = 0, y1;
        if (spec.Unit == "%") y1 = Math.Max(100, maxV);
        else y1 = maxV > 0 ? maxV * 1.15 : 1;
        return (x0, x1 == x0 ? x0 + 1 : x1, y0, y1 <= y0 ? y0 + 1 : y1);
    }

    private static string Inv(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    private static string YLabel(MetricSpec spec, double v)
    {
        if (spec.Unit == "%") return v.ToString("0", CultureInfo.InvariantCulture);
        double n = v; string[] u = ["", "K", "M", "G"]; int i = 0;
        while (Math.Abs(n) >= 1024 && i < 3) { n /= 1024; i++; }
        return n.ToString("0", CultureInfo.InvariantCulture) + u[i];
    }

    private static string TimeLabel(long ts) =>
        DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    // ---------- SVG (라이브러리 불필요, 텍스트 라벨 포함) ----------
    public static string RenderSvg(MetricSpec spec, string server, IReadOnlyList<(long Ts, double Value)> pts, int w, int h)
    {
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>");
        sb.Append($"<rect width='{w}' height='{h}' fill='#0c0e14'/>");

        if (pts.Count == 0)
        {
            sb.Append($"<text x='{w / 2 - 40}' y='{h / 2}' fill='#9aa0ac' font-size='13' font-family='sans-serif'>데이터 없음</text></svg>");
            return sb.ToString();
        }

        var (x0, x1, y0, y1) = Bounds(pts, spec);
        double Px(double x) => PadL + (x - x0) / (x1 - x0) * (w - PadL - PadR);
        double Py(double y) => h - PadB - (y - y0) / (y1 - y0) * (h - PadT - PadB);

        for (int i = 0; i <= 4; i++)
        {
            double y = y0 + (y1 - y0) * i / 4; double yy = Py(y);
            sb.Append($"<line x1='{PadL}' y1='{Inv(yy)}' x2='{w - PadR}' y2='{Inv(yy)}' stroke='#2a2e3a' stroke-width='1'/>");
            sb.Append($"<text x='4' y='{Inv(yy + 3)}' fill='#9aa0ac' font-size='11' font-family='sans-serif'>{YLabel(spec, y)}</text>");
        }
        AppendThresholdSvg(sb, spec.Warn, "#f9a825", y0, y1, w, Py);
        AppendThresholdSvg(sb, spec.Danger, "#c62828", y0, y1, w, Py);

        var poly = new StringBuilder();
        foreach (var p in pts) poly.Append($"{Inv(Px(p.Ts))},{Inv(Py(p.Value))} ");
        sb.Append($"<polyline fill='none' stroke='#4f8cff' stroke-width='2' points='{poly.ToString().Trim()}'/>");

        sb.Append($"<text x='{PadL}' y='12' fill='#e6e6e6' font-size='12' font-family='sans-serif'>{spec.Name} · {server}</text>");
        sb.Append($"<text x='{PadL}' y='{h - 6}' fill='#9aa0ac' font-size='11' font-family='sans-serif'>{TimeLabel(pts[0].Ts)}</text>");
        sb.Append($"<text x='{w - PadR - 52}' y='{h - 6}' fill='#9aa0ac' font-size='11' font-family='sans-serif'>{TimeLabel(pts[^1].Ts)}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void AppendThresholdSvg(StringBuilder sb, double v, string color, double y0, double y1, int w, Func<double, double> py)
    {
        if (v < y0 || v > y1) return;
        double yy = py(v);
        sb.Append($"<line x1='{PadL}' y1='{Inv(yy)}' x2='{w - PadR}' y2='{Inv(yy)}' stroke='{color}' stroke-width='1' stroke-dasharray='4 3'/>");
    }

    // ---------- PNG (SkiaSharp, MIT 무료, 라인+그리드+임계선) ----------
    public static byte[] RenderPng(MetricSpec spec, IReadOnlyList<(long Ts, double Value)> pts, int w, int h)
    {
        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#0c0e14"));

        if (pts.Count > 0)
        {
            var (x0, x1, y0, y1) = Bounds(pts, spec);
            float Px(double x) => (float)(PadL + (x - x0) / (x1 - x0) * (w - PadL - PadR));
            float Py(double y) => (float)(h - PadB - (y - y0) / (y1 - y0) * (h - PadT - PadB));

            using var grid = new SKPaint { Color = SKColor.Parse("#2a2e3a"), StrokeWidth = 1, IsAntialias = true };
            for (int i = 0; i <= 4; i++)
            {
                float yy = Py(y0 + (y1 - y0) * i / 4);
                canvas.DrawLine(PadL, yy, w - PadR, yy, grid);
            }

            DrawThresholdPng(canvas, spec.Warn, "#f9a825", y0, y1, w, Py);
            DrawThresholdPng(canvas, spec.Danger, "#c62828", y0, y1, w, Py);

            if (pts.Count >= 2)
            {
                using var line = new SKPaint
                {
                    Color = SKColor.Parse("#4f8cff"),
                    StrokeWidth = 2,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                };
                using var path = new SKPath();
                path.MoveTo(Px(pts[0].Ts), Py(pts[0].Value));
                for (int i = 1; i < pts.Count; i++) path.LineTo(Px(pts[i].Ts), Py(pts[i].Value));
                canvas.DrawPath(path, line);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static void DrawThresholdPng(SKCanvas canvas, double v, string color, double y0, double y1, int w, Func<double, float> py)
    {
        if (v < y0 || v > y1) return;
        float yy = py(v);
        using var paint = new SKPaint { Color = SKColor.Parse(color), StrokeWidth = 1, IsAntialias = true };
        canvas.DrawLine(PadL, yy, w - PadR, yy, paint);
    }
}
