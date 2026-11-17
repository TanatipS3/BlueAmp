using System.Drawing;
using System.Drawing.Drawing2D;

namespace BaudioApp.Views;

/// Central dark-theme palette + GDI+ helpers, mirroring the old WPF resource colors.
internal static class Theme
{
    public static readonly Color Accent      = FromHex("#00C853");
    public static readonly Color AccentLight = FromHex("#69F0AE");
    public static readonly Color Error       = FromHex("#FF5252");
    public static readonly Color Warn        = FromHex("#FF8C00");

    public static readonly Color TextPrimary   = FromHex("#F0F0F0");
    public static readonly Color TextSecondary = FromHex("#888888");
    public static readonly Color TextMuted     = FromHex("#555555");
    public static readonly Color TextFaint     = FromHex("#444444");

    public static readonly Color PopupBg   = FromHex("#1C1C1C");
    public static readonly Color WindowBg  = FromHex("#1A1A1A");
    public static readonly Color Surface   = FromHex("#222222");
    public static readonly Color Surface2  = FromHex("#2A2A2A");
    public static readonly Color Surface3  = FromHex("#303030");
    public static readonly Color BarTrack  = FromHex("#333333");
    public static readonly Color Border    = FromHex("#2E2E2E");
    public static readonly Color Divider   = FromHex("#26FFFFFF");

    public static readonly string FontFamily = GetUiFont();

    // Fonts are shared and live for the life of the app: the popup asks for a few
    // dozen per repaint, several repaints a second, so creating one per call churned
    // native font objects constantly. UI thread only; never dispose the result.
    private static readonly Dictionary<(float size, FontStyle style), Font> FontCache = new();

    public static Font F(float size, FontStyle style = FontStyle.Regular)
    {
        if (!FontCache.TryGetValue((size, style), out var font))
            FontCache[(size, style)] = font = new Font(FontFamily, size, style);
        return font;
    }

    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) // AARRGGBB
            return Color.FromArgb(
                Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16), Convert.ToInt32(hex.Substring(6, 2), 16));
        return Color.FromArgb(
            Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16));
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); path.CloseFigure(); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedBorder(Graphics g, RectangleF r, float radius, Color color, float thickness = 1f)
    {
        using var path = RoundedRect(r, radius);
        using var pen = new Pen(color, thickness);
        g.DrawPath(pen, path);
    }

    // Prefer the modern variable UI font when present, falling back to Segoe UI.
    private static string GetUiFont()
    {
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI" })
        {
            try { using var f = new Font(name, 9f); if (f.Name == name) return name; } catch { }
        }
        return "Segoe UI";
    }
}
