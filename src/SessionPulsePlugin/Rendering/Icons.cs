namespace Loupedeck.SessionPulsePlugin;

using System.Collections.Concurrent;
using System.Globalization;

/// <summary>
/// The SessionPulse icon set: original SVG artwork, generated here so colors and the spinner angle can
/// be set per call. Every icon is two-tone on purpose: the service may recolor monochrome SVGs
/// (Logi Actions SDK, csharp/icons/vector-images.md). Rasterized images are cached per SVG text and pixel
/// size, so drawing stays cheap and the class stays thread-safe.
/// </summary>
internal static class Icons
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    public static class Colors
    {
        public const string Blue = "#3B82F6";
        public const string Amber = "#FBBF24";
        public const string Green = "#22C55E";
        public const string Orange = "#F97316";
        public const string Gray = "#9CA3AF";
        public const string DarkGray = "#374151";
        public const string Red = "#EF4444";
        public const string Violet = "#A78BFA";
        public const string Glyph = "#0B0F14";
        public const string White = "#FFFFFF";
    }

    /// <summary>Rasterizes an SVG body (24x24 view box) to a square image of <paramref name="pixels"/>.</summary>
    public static BitmapImage Get(string body, int pixels)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"{pixels}\" height=\"{pixels}\">{body}</svg>";
        return Cache.GetOrAdd(svg, BitmapImage.FromSvg);
    }

    /// <summary>A round badge in <paramref name="color"/> with a dark glyph.</summary>
    public static string Badge(string color, string glyph) => $"<circle cx=\"12\" cy=\"12\" r=\"11\" fill=\"{color}\"/>{glyph}";

    // --- glyphs (drawn on a badge, 24x24 coordinates) -------------------------------------------

    /// <summary>A three-quarter ring, rotated by <paramref name="degrees"/> (spinner frames).</summary>
    public static string Spinner(int degrees) =>
        $"<g transform=\"rotate({degrees.ToString(CultureInfo.InvariantCulture)} 12 12)\">{Stroke("M12 5.5a6.5 6.5 0 1 1-6.5 6.5", Colors.Glyph, 2.6)}</g>";

    public static string Bell =>
        $"<path d=\"M12 5.2c-2.6 0-4.3 2-4.3 4.6v2.6l-1.4 2.2c-.3.5 0 1.1.6 1.1h10.2c.6 0 .9-.6.6-1.1l-1.4-2.2V9.8c0-2.6-1.7-4.6-4.3-4.6z\" fill=\"{Colors.Glyph}\"/>"
        + $"<circle cx=\"12\" cy=\"17.6\" r=\"1.7\" fill=\"{Colors.Glyph}\"/>";

    public static string Check => Stroke("M7 12.4l3.3 3.3L17 9", Colors.Glyph, 2.6);

    public static string DoubleCheck => Stroke("M4.8 12.6l2.9 2.9 6-6.3", Colors.Glyph, 2.2) + Stroke("M11.6 15.2l.4.3 6-6.3", Colors.Glyph, 2.2);

    public static string Clock => Stroke("M12 6.5a5.5 5.5 0 1 0 0 11a5.5 5.5 0 1 0 0-11z", Colors.Glyph, 2) + Stroke("M12 9v3.3l2.2 1.5", Colors.Glyph, 2);

    public static string Pause =>
        $"<rect x=\"8.6\" y=\"7.5\" width=\"2.4\" height=\"9\" rx=\"1\" fill=\"{Colors.Glyph}\"/><rect x=\"13\" y=\"7.5\" width=\"2.4\" height=\"9\" rx=\"1\" fill=\"{Colors.Glyph}\"/>";

    public static string Calendar =>
        $"<rect x=\"6\" y=\"7.2\" width=\"12\" height=\"10.6\" rx=\"2\" fill=\"none\" stroke=\"{Colors.Glyph}\" stroke-width=\"2\"/>"
        + $"<path d=\"M6 10.6h12\" stroke=\"{Colors.Glyph}\" stroke-width=\"2\"/>"
        + Stroke("M9 5.6v3M15 5.6v3", Colors.Glyph, 2)
        + $"<rect x=\"8.6\" y=\"12.8\" width=\"2.6\" height=\"2.4\" rx=\".6\" fill=\"{Colors.Glyph}\"/>";

    public static string Microphone =>
        $"<rect x=\"9.6\" y=\"5\" width=\"4.8\" height=\"8.6\" rx=\"2.4\" fill=\"{Colors.Glyph}\"/>"
        + Stroke("M7.2 11.4a4.8 4.8 0 0 0 9.6 0M12 16.3v2.6", Colors.Glyph, 1.9);

    public static string Cross => Stroke("M8.2 8.2l7.6 7.6M15.8 8.2l-7.6 7.6", Colors.Glyph, 2.6);

    /// <summary>Two arrows pointing at each other: compacting.</summary>
    public static string Compress => Stroke("M12 5v5M9.5 7.6L12 10l2.5-2.4M12 19v-5M9.5 16.4L12 14l2.5 2.4M6.5 12h11", Colors.Glyph, 2);

    /// <summary>A shield with a check mark: a permission request.</summary>
    public static string Shield =>
        $"<path d=\"M12 4.6l5.6 2.2v4.4c0 3.6-2.4 6.5-5.6 7.8-3.2-1.3-5.6-4.2-5.6-7.8V6.8z\" fill=\"{Colors.Glyph}\"/>"
        + $"<path d=\"M9.4 11.8l1.9 1.9 3.4-3.6\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"1.7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";

    /// <summary>Fast forward: jump to the next session that needs you.</summary>
    public static string Next =>
        $"<path d=\"M6.5 7.5v9l5.5-4.5zM12.5 7.5v9l5.5-4.5z\" fill=\"{Colors.Glyph}\" stroke=\"{Colors.Glyph}\" stroke-width=\"1\" stroke-linejoin=\"round\"/>";

    /// <summary>A curved arrow back: hand the decision back to Claude.</summary>
    public static string Return => Stroke("M9.5 8L6.5 11l3 3M6.8 11h6.7a4 4 0 0 1 0 8h-1.5", Colors.Glyph, 2.2);

    /// <summary>A speedometer: usage limits.</summary>
    public static string Gauge => Stroke("M5.6 15.5a6.6 6.6 0 1 1 12.8 0", Colors.Glyph, 2.2) + Stroke("M12 14l3-4.2", Colors.Glyph, 2.2)
        + $"<circle cx=\"12\" cy=\"14\" r=\"1.6\" fill=\"{Colors.Glyph}\"/>";

    public static string Exclamation =>
        Stroke("M12 6.5v6.5", Colors.Glyph, 2.8) + $"<circle cx=\"12\" cy=\"17\" r=\"1.6\" fill=\"{Colors.Glyph}\"/>";

    // --- source icons (no badge, two-tone gray) --------------------------------------------------

    /// <summary>Claude Code: a terminal window with a prompt.</summary>
    public static string Terminal =>
        $"<rect x=\"2.5\" y=\"4\" width=\"19\" height=\"16\" rx=\"3\" fill=\"{Colors.DarkGray}\" stroke=\"{Colors.Gray}\" stroke-width=\"1.6\"/>"
        + Stroke("M7 9.5l3 2.5-3 2.5M12.5 15h4.5", Colors.Gray, 1.8);

    /// <summary>Cowork: a briefcase.</summary>
    public static string Briefcase =>
        $"<path d=\"M9 7V5.6C9 4.7 9.7 4 10.6 4h2.8c.9 0 1.6.7 1.6 1.6V7\" fill=\"none\" stroke=\"{Colors.Gray}\" stroke-width=\"1.6\"/>"
        + $"<rect x=\"3\" y=\"7\" width=\"18\" height=\"13\" rx=\"2.6\" fill=\"{Colors.Gray}\"/>"
        + $"<path d=\"M3 12.2h18\" stroke=\"{Colors.DarkGray}\" stroke-width=\"1.4\"/>";

    /// <summary>Unread marker: blue dot with a dark ring so it stands out on any background.</summary>
    public static string UnreadDot => $"<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"{Colors.Glyph}\"/><circle cx=\"12\" cy=\"12\" r=\"7.5\" fill=\"{Colors.Amber}\"/>";

    /// <summary>A rounded bar, used for chips and the progress track (stretched to any aspect ratio).</summary>
    public static BitmapImage Pill(string color, double opacity, int width, int height)
    {
        var o = opacity.ToString("0.##", CultureInfo.InvariantCulture);
        var r = (height / 2.0).ToString("0.##", CultureInfo.InvariantCulture);
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\">"
            + $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" rx=\"{r}\" fill=\"{color}\" fill-opacity=\"{o}\"/>"
            + "<rect x=\"0\" y=\"0\" width=\"0.01\" height=\"0.01\" fill=\"#000000\" fill-opacity=\"0\"/></svg>";
        return Cache.GetOrAdd(svg, BitmapImage.FromSvg);
    }

    private static string Stroke(string d, string color, double width) =>
        $"<path d=\"{d}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{width.ToString(CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";
}
