namespace Loupedeck.SessionPulsePlugin;

using System.Globalization;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Status;
using SessionPulse.Core.Usage;

/// <summary>Symbols for folder tiles and action keys.</summary>
internal enum TileIcon
{
    Working,
    Waiting,
    Done,
    Calendar,
    Microphone,
    Next,
    Approvals,
    Allow,
    AllowSession,
    AllowAlways,
    Deny,
    AskInClaude,
}

/// <summary>
/// Draws the keys. Layout in units of 1/100 of the key width:
/// <list type="bullet">
/// <item>Session key: state badge (top left, the main signal), elapsed time next to it, source icon
/// (top right), title in up to three lines, and at the bottom either a pill with the reason when the
/// session needs you, or a progress bar.</item>
/// <item>Tile: a badge (top left when there is a count, else big and centered), a big number and a
/// label; dimmed when there is nothing to show.</item>
/// </list>
/// Icons are SVG (see <see cref="Icons"/>); texts use the service's default font. Stateless apart from the
/// thread-safe icon cache, so it can be called from any thread.
/// </summary>
internal sealed class KeyRenderer
{
    private static readonly BitmapColor BaseBackground = new(15, 19, 26);
    private static readonly BitmapColor TitleColor = new(243, 244, 246);
    private static readonly BitmapColor DimText = new(156, 163, 175);
    private static readonly BitmapColor FaintText = new(55, 65, 81);
    private static readonly BitmapColor DarkText = new(11, 15, 20);

    public BitmapImage Render(KeyModel model, PluginImageSize imageSize, int frame)
    {
        using var b = new BitmapBuilder(imageSize);
        var u = b.Width / 100f;
        var style = Style.For(model.State, frame);
        b.Clear(style.Background);

        if (model.State == KeyState.Empty)
        {
            b.DrawText(model.Number.ToString(CultureInfo.InvariantCulture), 0, 0, b.Width, b.Height, FaintText, Px(34, u));
            return b.ToImage();
        }

        // Header: badge, time, source.
        var badge = Px(26, u);
        b.DrawImage(Icons.Get(Icons.Badge(style.Accent, StateGlyph(model.State, frame)), badge), Px(5, u), Px(5, u), badge, badge);
        if (model.Elapsed.Length > 0)
        {
            b.DrawText(model.Elapsed, Px(33, u), Px(4, u), Px(44, u), Px(28, u), DimText, Px(15, u));
        }

        var source = Px(18, u);
        var corner = model.Unread ? Icons.UnreadDot : model.Source == SessionSource.Cowork ? Icons.Briefcase : Icons.Terminal;
        b.DrawImage(Icons.Get(corner, source), Px(78, u), Px(9, u), source, source);

        // Footer: reason pill (needs you) or progress bar.
        var showReason = model.State is KeyState.Waiting or KeyState.Stuck or KeyState.Failed or KeyState.Compacting;
        var titleBottom = 92;
        if (showReason)
        {
            int x = Px(8, u), y = Px(74, u), w = Px(84, u), h = Px(20, u);
            b.DrawImage(Icons.Pill(style.Accent, 1, w, h), x, y, w, h);
            var reason = TextLayout.Wrap(model.Detail, Math.Max(4, (int)(w / (Px(13, u) * 0.58f))), 1);
            b.DrawText(reason.Count > 0 ? reason[0] : string.Empty, x, y, w, h, DarkText, Px(13, u));
            titleBottom = 72;
        }
        else if (model.Progress is { } progress)
        {
            int x = Px(10, u), y = Px(88, u), w = Px(80, u), h = Math.Max(3, Px(5, u));
            b.DrawImage(Icons.Pill(Icons.Colors.White, 0.16, w, h), x, y, w, h);
            var filled = Math.Max(h, (int)Math.Round(w * progress));
            b.DrawImage(Icons.Pill(style.Accent, 1, filled, h), x, y, filled, h);
            titleBottom = 85;
        }

        // Title: as many lines as fit between header and footer, no word broken if possible.
        var top = 34f;
        var lineUnits = 18.5f;
        var maxLines = Math.Clamp((int)((titleBottom - top) / lineUnits), 1, 3);
        var (font, maxChars) = FitFont(model.Title, b.Width, u, maxLines);
        var lines = TextLayout.Wrap(model.Title, maxChars, maxLines);
        var lineHeight = lineUnits * u;
        var start = (top * u) + ((((titleBottom - top) * u) - (lines.Count * lineHeight)) / 2);
        for (var i = 0; i < lines.Count; i++)
        {
            b.DrawText(lines[i], Px(3, u), (int)(start + (i * lineHeight)), Px(94, u), (int)lineHeight, TitleColor, font);
        }

        return b.ToImage();
    }

    /// <summary>
    /// Folder tile or feature key: badge, big number (if <paramref name="count"/> is not null) and a
    /// label. Dimmed when <paramref name="active"/> is false (nothing to show / not recording).
    /// </summary>
    public BitmapImage RenderTile(TileIcon icon, int? count, string label, bool active, PluginImageSize imageSize, int frame) =>
        this.RenderTile(icon, count, label, active, imageSize, frame, null);

    private BitmapImage RenderTile(TileIcon icon, int? count, string label, bool active, PluginImageSize imageSize, int frame, string? glyphOverride)
    {
        using var b = new BitmapBuilder(imageSize);
        var u = b.Width / 100f;
        var (background, accent, glyph) = TileStyle(icon, active, frame);
        glyph = glyphOverride ?? glyph;
        b.Clear(background);
        var dimmed = !active && icon != TileIcon.Microphone;

        if (count is { } n)
        {
            // Badge top left, not top center: Options+ draws a white tab over the top center of folder
            // keys (about 35 % wide, 15 % high; docs/LESSONS.md), and most counted tiles are folders.
            var badge = Px(24, u);
            b.DrawImage(Icons.Get(Icons.Badge(accent, glyph), badge), Px(4, u), Px(4, u), badge, badge);
            b.DrawText(n.ToString(CultureInfo.InvariantCulture), 0, Px(24, u), b.Width, Px(48, u), dimmed ? FaintText : TitleColor, Px(34, u));
            b.DrawText(label, 0, Px(74, u), b.Width, Px(22, u), dimmed ? FaintText : DimText, Px(14, u));
        }
        else
        {
            var badge = Px(44, u);
            b.DrawImage(Icons.Get(Icons.Badge(accent, glyph), badge), (b.Width - badge) / 2, Px(9, u), badge, badge);
            var font = Px(14, u);
            var lines = TextLayout.Wrap(label, Math.Max(4, (int)(b.Width * 0.94f / (font * 0.55f))), 2);
            var lineHeight = 17 * u;
            var y = (lines.Count == 1 ? 66 : 58) * u;
            for (var i = 0; i < lines.Count; i++)
            {
                b.DrawText(lines[i], 0, (int)(y + (i * lineHeight)), b.Width, (int)lineHeight, dimmed ? FaintText : TitleColor, font);
            }
        }

        return b.ToImage();
    }

    /// <summary>
    /// The request in the approvals folder: what wants to run (tool and command or file), with a red
    /// background and a "risky" pill for destructive commands.
    /// </summary>
    public BitmapImage RenderApproval(PendingApproval request, Texts texts, PluginImageSize imageSize)
    {
        using var b = new BitmapBuilder(imageSize);
        var u = b.Width / 100f;
        var high = request.Risk == ApprovalRisk.High;
        b.Clear(high ? new BitmapColor(60, 12, 12) : BaseBackground);

        var badge = Px(24, u);
        b.DrawImage(Icons.Get(Icons.Badge(high ? Icons.Colors.Red : Icons.Colors.Amber, Icons.Shield), badge), Px(5, u), Px(5, u), badge, badge);
        b.DrawText(request.Tool, Px(31, u), Px(5, u), Px(66, u), Px(24, u), TitleColor, Px(request.Tool.Length > 7 ? 12 : 15, u));

        var bottom = 96;
        if (high)
        {
            int x = Px(8, u), y = Px(76, u), w = Px(84, u), h = Px(19, u);
            b.DrawImage(Icons.Pill(Icons.Colors.Red, 1, w, h), x, y, w, h);
            b.DrawText(texts.Risky, x, y, w, h, TitleColor, Px(13, u));
            bottom = 74;
        }

        var font = Px(12, u);
        var maxLines = high ? 3 : 4;
        var lines = TextLayout.Wrap(request.Preview.Length > 0 ? request.Preview : request.Tool, Math.Max(4, (int)(b.Width * 0.94f / (font * 0.55f))), maxLines);
        var lineHeight = 14.5f * u;
        var top = 32 * u;
        var start = top + ((((bottom * u) - top) - (lines.Count * lineHeight)) / 2);
        for (var i = 0; i < lines.Count; i++)
        {
            b.DrawText(lines[i], Px(3, u), (int)(start + (i * lineHeight)), Px(94, u), (int)lineHeight, DimText, font);
        }

        return b.ToImage();
    }

    /// <summary>
    /// Usage limits: one row per window (5 hours, week) with label, percent, a bar and the time to the
    /// reset. Green below 80 %, amber below 95 %, red above.
    /// </summary>
    public BitmapImage RenderUsage(UsageSnapshot? usage, long now, Texts texts, PluginImageSize imageSize)
    {
        if (usage is null || (usage.FiveHour is null && usage.SevenDay is null && usage.Spend is null))
        {
            return this.RenderTile(TileIcon.Next, null, texts.NoData, false, imageSize, 0, Icons.Gauge);
        }

        using var b = new BitmapBuilder(imageSize);
        var u = b.Width / 100f;
        b.Clear(BaseBackground);
        var rows = new List<(string Label, UsageWindow Window)>();
        if (usage.FiveHour is { } five) rows.Add((texts.FiveHours, five.AsOf(now)));
        if (usage.SevenDay is { } week) rows.Add((texts.Week, week.AsOf(now)));
        if (rows.Count < 2 && usage.Spend is { } spend) rows.Add(("$", spend.AsOf(now)));

        var rowHeight = 48f;
        var y0 = rows.Count == 1 ? 27f : 5f;
        for (var i = 0; i < rows.Count; i++)
        {
            var (label, window) = rows[i];
            var y = (y0 + (i * rowHeight)) * u;
            var color = window.Percent >= 95 ? Icons.Colors.Red : window.Percent >= 80 ? Icons.Colors.Amber : Icons.Colors.Green;
            b.DrawText(label, Px(4, u), (int)y, Px(30, u), Px(20, u), DimText, Px(14, u));
            b.DrawText(UsageStore.Percent(window.Percent), Px(30, u), (int)y, Px(66, u), Px(20, u), TitleColor, Px(16, u));
            int x = Px(6, u), by = (int)(y + (21 * u)), w = Px(88, u), h = Math.Max(3, Px(6, u));
            b.DrawImage(Icons.Pill(Icons.Colors.White, 0.16, w, h), x, by, w, h);
            var filled = Math.Max(h, (int)Math.Round(w * Math.Clamp(window.Percent / 100, 0, 1)));
            b.DrawImage(Icons.Pill(color, 1, filled, h), x, by, filled, h);
            var left = UsageStore.TimeLeft(window.ResetsAt, now);
            if (left.Length > 0)
            {
                b.DrawText(texts.ResetsIn(left), 0, (int)(y + (28 * u)), b.Width, Px(14, u), DimText, Px(11, u));
            }
        }

        return b.ToImage();
    }

    /// <summary>Shown when drawing fails: an amber badge with an exclamation mark.</summary>
    public BitmapImage RenderError(PluginImageSize imageSize)
    {
        using var b = new BitmapBuilder(imageSize);
        b.Clear(BaseBackground);
        var size = b.Width / 2;
        b.DrawImage(Icons.Get(Icons.Badge(Icons.Colors.Amber, Icons.Exclamation), size), size / 2, size / 2, size, size);
        return b.ToImage();
    }

    private static int Px(float units, float u) => Math.Max(1, (int)Math.Round(units * u));

    private static string StateGlyph(KeyState state, int frame) => state switch
    {
        KeyState.Working => Icons.Spinner((frame * 45) % 360),
        KeyState.Waiting => Icons.Bell,
        KeyState.Done => Icons.Check,
        KeyState.Stuck => Icons.Clock,
        KeyState.Compacting => Icons.Compress,
        KeyState.Failed => Icons.Exclamation,
        _ => Icons.Pause,
    };

    /// <summary>
    /// Largest font (16 down to 11 units) at which the title fits without breaking a word; the smallest
    /// one otherwise. There is no text measuring in the SDK, so the width per character is an estimate
    /// calibrated on rendered keys (about 0.55 of the font size).
    /// </summary>
    private static (int FontSize, int MaxChars) FitFont(string text, int width, float u, int maxLines)
    {
        (int, int) candidate = default;
        for (var units = 16; units >= 11; units--)
        {
            var font = Px(units, u);
            var maxChars = Math.Max(4, (int)(width * 0.94f / (font * 0.55f)));
            candidate = (font, maxChars);
            if (TextLayout.FitsWithoutBreaking(text, maxChars, maxLines))
            {
                break;
            }
        }

        return candidate;
    }

    private static (BitmapColor Background, string Accent, string Glyph) TileStyle(TileIcon icon, bool active, int frame)
    {
        var accent = icon switch
        {
            TileIcon.Working => Icons.Colors.Blue,
            TileIcon.Waiting or TileIcon.Next or TileIcon.Approvals => Icons.Colors.Amber,
            TileIcon.Done or TileIcon.Allow or TileIcon.AllowAlways => Icons.Colors.Green,
            TileIcon.Calendar or TileIcon.AllowSession => Icons.Colors.Blue,
            TileIcon.Deny => Icons.Colors.Red,
            TileIcon.AskInClaude => Icons.Colors.Gray,
            _ => active ? Icons.Colors.Red : Icons.Colors.Gray,
        };
        var glyph = icon switch
        {
            TileIcon.Working => Icons.Spinner(active ? (frame * 45) % 360 : 0),
            TileIcon.Waiting => Icons.Bell,
            TileIcon.Done or TileIcon.Allow or TileIcon.AllowSession => Icons.Check,
            TileIcon.AllowAlways => Icons.DoubleCheck,
            TileIcon.Calendar => Icons.Calendar,
            TileIcon.Next => Icons.Next,
            TileIcon.Approvals => Icons.Shield,
            TileIcon.Deny => Icons.Cross,
            TileIcon.AskInClaude => Icons.Return,
            _ => Icons.Microphone,
        };

        if (!active && icon != TileIcon.Microphone)
        {
            return (BaseBackground, Icons.Colors.DarkGray, glyph);
        }

        var background = icon switch
        {
            TileIcon.Waiting or TileIcon.Approvals => Style.For(KeyState.Waiting, frame).Background,
            TileIcon.Microphone when active => frame % 2 == 0 ? new BitmapColor(58, 14, 14) : new BitmapColor(92, 20, 20),
            _ => BaseBackground,
        };
        return (background, accent, glyph);
    }

    private readonly record struct Style(BitmapColor Background, string Accent)
    {
        public static Style For(KeyState state, int frame) => state switch
        {
            KeyState.Working => new(BaseBackground, Icons.Colors.Blue),
            // Waiting pulses between two amber shades: the key that needs you is the one that moves.
            KeyState.Waiting => new(frame % 2 == 0 ? new BitmapColor(46, 34, 6) : new BitmapColor(84, 62, 8), Icons.Colors.Amber),
            KeyState.Done => new(new BitmapColor(10, 30, 20), Icons.Colors.Green),
            KeyState.Stuck => new(new BitmapColor(36, 20, 8), Icons.Colors.Orange),
            KeyState.Compacting => new(BaseBackground, Icons.Colors.Violet),
            KeyState.Failed => new(new BitmapColor(48, 12, 12), Icons.Colors.Red),
            KeyState.Idle => new(BaseBackground, Icons.Colors.Gray),
            _ => new(BaseBackground, Icons.Colors.DarkGray),
        };
    }
}
