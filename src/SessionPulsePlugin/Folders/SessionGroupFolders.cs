namespace Loupedeck.SessionPulsePlugin;

using SessionPulse.Core;
using SessionPulse.Core.Presentation;

/// <summary>
/// Counter folder for one session group: the tile shows symbol and count (gray at 0), the page lists the
/// matching sessions. The "done" page ends with a key that acknowledges all finished sessions.
/// </summary>
public abstract class SessionGroupFolder : SessionPulseFolder
{
    internal const string AcknowledgeAllParameter = "clear-all";

    private const int MaxKeys = 8;

    private readonly SessionGroup group;

    private protected SessionGroupFolder(SessionGroup group, string displayName)
        : base(displayName, new TileState(IconFor(group), 0, Texts.Current.Group(group), false))
    {
        this.group = group;
    }

    internal override (IReadOnlyList<FolderItem> Items, TileState Tile) Compute(Dashboard dashboard, Texts texts)
    {
        var withClearAll = this.group == SessionGroup.Done;
        var keys = dashboard.GroupKeys(this.group, withClearAll ? MaxKeys - 1 : MaxKeys);
        var items = keys.Select(k => new FolderItem(k.SessionId!, k)).ToList();
        if (withClearAll && items.Count > 0)
        {
            items.Add(new FolderItem(AcknowledgeAllParameter, null, texts.AcknowledgeAll, TileIcon.Done));
        }

        var count = dashboard.Counts[this.group];
        return (items, new TileState(IconFor(this.group), count, texts.Group(this.group), count > 0));
    }

    internal override void OnItemPressed(FolderItem item)
    {
        if (item.Parameter == AcknowledgeAllParameter)
        {
            this.Owner.AcknowledgeAllFinished();
        }
        else
        {
            this.Owner.OpenSession(item.Parameter);
        }
    }

    private static TileIcon IconFor(SessionGroup group) => group switch
    {
        SessionGroup.Working => TileIcon.Working,
        SessionGroup.Waiting => TileIcon.Waiting,
        _ => TileIcon.Done,
    };
}

// One class per folder: parameterized actions are unreliable in Options+ (docs/LESSONS.md).

public sealed class WorkingSessionsFolder : SessionGroupFolder
{
    public WorkingSessionsFolder()
        : base(SessionGroup.Working, "Working sessions")
    {
    }
}

public sealed class WaitingSessionsFolder : SessionGroupFolder
{
    public WaitingSessionsFolder()
        : base(SessionGroup.Waiting, "Waiting sessions")
    {
    }
}

public sealed class DoneSessionsFolder : SessionGroupFolder
{
    public DoneSessionsFolder()
        : base(SessionGroup.Done, "Done sessions")
    {
    }
}
