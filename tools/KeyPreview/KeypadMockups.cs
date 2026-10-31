using Loupedeck;
using Loupedeck.SessionPulsePlugin;
using SessionPulse.Core;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Status;
using SkiaSharp;

/// <summary>
/// Renders the simulated keypad pictures for the README: real key images with made-up sessions, on a
/// plain 3x3 panel. Not a photo of the device, and on purpose without any Logitech artwork.
/// </summary>
internal static class KeypadMockups
{
    private const PluginImageSize KeySize = PluginImageSize.Width116;
    private const int Key = 116;
    private const int Gap = 12;
    private const int Margin = 24;

    public static int Write(string output)
    {
        Directory.CreateDirectory(output);
        var en = Texts.English;
        var renderer = new KeyRenderer();

        BitmapImage Session(int number, KeyState state, SessionSource source, string title, string detail, string time, int done = 0, int total = 0) =>
            renderer.Render(new KeyModel(number, state, number.ToString(), source, title, detail, time, done, total), KeySize, 0);

        BitmapImage Tile(TileIcon icon, int? count, string label, bool active = true) =>
            renderer.RenderTile(icon, count, label, active, KeySize, 1);

        Save(output, "keypad-sessions.png",
        [
            Session(1, KeyState.Working, SessionSource.Code, "Fix the login page", "Bash", "3m", 2, 5),
            Session(2, KeyState.Waiting, SessionSource.Code, "Update the API docs", en.Detail(StatusDetail.Permission, null), "45s"),
            Session(3, KeyState.Done, SessionSource.Code, "Refactor the auth module", en.Detail(StatusDetail.Done, null), "12m", 7, 7),
            Session(4, KeyState.Working, SessionSource.Code, "Add dark mode", "Edit", "8m", 4, 6),
            Session(5, KeyState.Waiting, SessionSource.Code, "Plan the release", en.Detail(StatusDetail.Question, null), "2m"),
            Session(6, KeyState.Stuck, SessionSource.Code, "my-app", en.Stuck, "1:05h"),
            Tile(TileIcon.Working, 2, en.Group(SessionGroup.Working)),
            Tile(TileIcon.Waiting, 2, en.Group(SessionGroup.Waiting)),
            Tile(TileIcon.Done, 1, en.Group(SessionGroup.Done)),
        ],
        folderKeys: [6, 7, 8]);

        var request = new PendingApproval("r1", "s1", "Bash", "rm -rf build/ && npm run build", ApprovalRisk.High, true, 0, 0);
        Save(output, "keypad-approval.png",
        [
            renderer.RenderApproval(request, en, KeySize),
            Tile(TileIcon.Allow, null, en.AllowOnce),
            Tile(TileIcon.AllowSession, null, en.AllowSession),
            Tile(TileIcon.AllowAlways, null, en.AllowAlways),
            Tile(TileIcon.Deny, null, en.Deny),
            Tile(TileIcon.AskInClaude, null, en.DecideInClaude),
            null,
            null,
            null,
        ]);

        Console.WriteLine($"Rendered keypad pictures to {Path.GetFullPath(output)}");
        return 0;
    }

    private static void Save(string output, string name, BitmapImage?[] keys, int[]? folderKeys = null)
    {
        var size = (2 * Margin) + (3 * Key) + (2 * Gap);
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var panel = new SKPaint { Color = new SKColor(0x26, 0x28, 0x2D), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(0, 0, size, size), 28, 28, panel);

        using var clip = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        using var blank = new SKPaint { Color = new SKColor(0x0D, 0x11, 0x17), IsAntialias = true };
        using var tab = new SKPaint { Color = SKColors.White, IsAntialias = true };
        for (var i = 0; i < keys.Length; i++)
        {
            var x = Margin + ((i % 3) * (Key + Gap));
            var y = Margin + ((i / 3) * (Key + Gap));
            var rect = new SKRect(x, y, x + Key, y + Key);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(rect, 10, 10), SKClipOperation.Intersect, true);
            if (keys[i] is { } key)
            {
                using var bitmap = SKBitmap.Decode(key.ToArray());
                canvas.DrawBitmap(bitmap, rect, clip);
            }
            else
            {
                canvas.DrawRect(rect, blank);
            }

            if (folderKeys?.Contains(i) == true)
            {
                // Options+ draws this white tab over folder keys; the plugin's images don't contain it (docs/LESSONS.md #45).
                var tabWidth = Key * 0.35f;
                var tabHeight = Key * 0.075f;
                var left = x + ((Key - tabWidth) / 2);
                canvas.DrawRoundRect(new SKRect(left, y - tabHeight, left + tabWidth, y + tabHeight), 6, 6, tab);
            }

            canvas.Restore();
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, name), data.ToArray());
    }
}
