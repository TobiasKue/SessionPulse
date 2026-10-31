namespace Loupedeck.SessionPulsePlugin;

using SessionPulse.Core;
using SessionPulse.Core.Presentation;

/// <summary>
/// Scheduled tasks: newest non-archived run per task, unread first. The tile counts unread runs.
/// Pressing a run opens it, scrolls it to the top and marks it read.
/// </summary>
public sealed class ScheduledTasksFolder : SessionPulseFolder
{
    private const int MaxKeys = 8;

    public ScheduledTasksFolder()
        : base("Scheduled tasks", new TileState(TileIcon.Calendar, 0, Texts.Current.Scheduled, false))
    {
    }

    internal override (IReadOnlyList<FolderItem> Items, TileState Tile) Compute(Dashboard dashboard, Texts texts)
    {
        var items = dashboard.ScheduledKeys(MaxKeys).Select(k => new FolderItem(k.RunKey, k.Model)).ToList();
        var unread = dashboard.UnreadScheduledRuns;
        return (items, new TileState(TileIcon.Calendar, unread, texts.Scheduled, unread > 0));
    }

    internal override void OnItemPressed(FolderItem item) => this.Owner.OpenScheduledRun(item.Parameter);
}
