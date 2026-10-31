using Loupedeck;
using Loupedeck.SessionPulsePlugin;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Status;
using SessionPulse.Core.Usage;

// Renders every key state, tile and size (plus a second frame of animated states) to PNG files.
// Fails (exit code 1) if any key cannot be drawn, so CI can use it as a smoke test.
var output = args.Length > 0 ? args[0] : Path.Combine("artifacts", "key-preview");
Directory.CreateDirectory(output);
foreach (var old in Directory.GetFiles(output, "*.png"))
{
    File.Delete(old);
}

var de = Texts.German;
KeyModel[] keys =
[
    new(1, KeyState.Working, "a", SessionSource.Code, "SessionPulse", "Bash", "3m", 2, 5),
    new(2, KeyState.Waiting, "b", SessionSource.Cowork, "Wochenbericht für das Team erstellen", de.Detail(StatusDetail.Permission, null), "45s", 0, 0),
    new(3, KeyState.Done, "c", SessionSource.Code, "Refactor the authentication module and update all tests", de.Detail(StatusDetail.Done, null), "12m", 7, 7),
    new(4, KeyState.Stuck, "d", SessionSource.Code, "my-app", de.Stuck, "1:05h", 0, 0),
    new(5, KeyState.Idle, "e", SessionSource.Cowork, "Inbox triage", de.Detail(StatusDetail.Interrupted, null), string.Empty, 0, 0),
    new(6, KeyState.Waiting, "f", SessionSource.Code, "Supercalifragilisticexpialidocious", de.Detail(StatusDetail.Question, null), "2m", 0, 0),
    new(7, KeyState.Done, "g", SessionSource.Cowork, "Morning digest", de.Detail(StatusDetail.Done, null), "3h", 0, 0, Unread: true),
    new(8, KeyState.Compacting, "h", SessionSource.Code, "SessionPulse", de.Detail(StatusDetail.Compacting, null), "8m", 0, 0),
    new(9, KeyState.Failed, "i", SessionSource.Cowork, "Quarterly numbers", de.Detail(StatusDetail.Limit, null), "4m", 0, 0),
    KeyModel.Empty(10),
];

(string Name, TileIcon Icon, int? Count, string Label, bool Active)[] tiles =
[
    ("working", TileIcon.Working, 3, de.Group(SessionPulse.Core.SessionGroup.Working), true),
    ("waiting", TileIcon.Waiting, 1, de.Group(SessionPulse.Core.SessionGroup.Waiting), true),
    ("done", TileIcon.Done, 5, de.Group(SessionPulse.Core.SessionGroup.Done), true),
    ("done-zero", TileIcon.Done, 0, de.Group(SessionPulse.Core.SessionGroup.Done), false),
    ("clear-all", TileIcon.Done, null, de.AcknowledgeAll, true),
    ("scheduled", TileIcon.Calendar, 2, de.Scheduled, true),
    ("dictation", TileIcon.Microphone, null, de.Dictation, false),
    ("dictating", TileIcon.Microphone, null, de.DictationActive, true),
    ("next", TileIcon.Next, 2, de.NextWaiting, true),
    ("next-zero", TileIcon.Next, 0, de.NothingWaiting, false),
    ("approvals", TileIcon.Approvals, 1, de.Approvals, true),
    ("allow", TileIcon.Allow, null, de.AllowOnce, true),
    ("allow-session", TileIcon.AllowSession, null, de.AllowSession, true),
    ("allow-always", TileIcon.AllowAlways, null, de.AllowAlways, true),
    ("deny", TileIcon.Deny, null, de.Deny, true),
    ("ask", TileIcon.AskInClaude, null, de.DecideInClaude, true),
];

PendingApproval[] approvals =
[
    new("r1", "s1", "Bash", "rm -rf build/ && npm run build", ApprovalRisk.High, true, 0, 0),
    new("r2", "s1", "Edit", "Dashboard.cs", ApprovalRisk.Edit, false, 0, 0),
    new("r3", "s1", "WebFetch", "https://code.claude.com/docs/en/hooks", ApprovalRisk.Normal, false, 0, 0),
];
const long previewNow = 1_800_000_000;
UsageSnapshot?[] usages =
[
    new(previewNow, new UsageWindow(42, previewNow + 4800), new UsageWindow(18, previewNow + (3 * 86400)), null),
    new(previewNow, new UsageWindow(93, previewNow + 1500), new UsageWindow(81, previewNow + 86400), null),
    null,
];

var renderer = new KeyRenderer();
var failures = 0;
void Save(string name, Func<BitmapImage> draw)
{
    try
    {
        File.WriteAllBytes(Path.Combine(output, name + ".png"), draw().ToArray());
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
    }
}

foreach (var size in new[] { PluginImageSize.Width60, PluginImageSize.Width90, PluginImageSize.Width116 })
{
    var s = size.ToString().ToLowerInvariant();
    foreach (var model in keys)
    {
        foreach (var frame in model.IsAnimated ? new[] { 0, 1 } : [0])
        {
            Save($"{s}-key{model.Number:00}-{model.State.ToString().ToLowerInvariant()}-f{frame}", () => renderer.Render(model, size, frame));
        }
    }

    for (var i = 0; i < tiles.Length; i++)
    {
        var t = tiles[i];
        Save($"{s}-tile{i + 1:00}-{t.Name}", () => renderer.RenderTile(t.Icon, t.Count, t.Label, t.Active, size, 1));
    }

    for (var i = 0; i < approvals.Length; i++)
    {
        var a = approvals[i];
        Save($"{s}-tile{tiles.Length + i + 1:00}-approval-{a.Risk.ToString().ToLowerInvariant()}", () => renderer.RenderApproval(a, de, size));
    }

    for (var i = 0; i < usages.Length; i++)
    {
        var usage = usages[i];
        Save($"{s}-tile{tiles.Length + approvals.Length + i + 1:00}-usage{i + 1}", () => renderer.RenderUsage(usage, previewNow, de, size));
    }

    Save($"{s}-zz-error", () => renderer.RenderError(size));
}

Console.WriteLine($"Rendered keys to {Path.GetFullPath(output)} ({failures} failures)");
return failures == 0 ? 0 : 1;
