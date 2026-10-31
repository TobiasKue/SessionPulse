namespace Loupedeck.SessionPulsePlugin;

using SessionPulse.Core;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Presentation;

/// <summary>
/// Keypad approvals (opt-in, APPROVALS=1 in sessionpulse.conf). The tile counts held permission
/// requests; the page shows the oldest one (tool and command, risky ones in red) and the answers:
/// allow once, allow for the session, always allow (only when Claude suggested a rule), deny, or hand
/// it back to Claude's own dialog. Pressing the request itself opens its session.
/// </summary>
public sealed class ApprovalsFolder : SessionPulseFolder
{
    private const string Separator = "__";

    public ApprovalsFolder()
        : base("Approvals", new TileState(TileIcon.Approvals, 0, Texts.Current.Approvals, false))
    {
    }

    internal override (IReadOnlyList<FolderItem> Items, TileState Tile) Compute(Dashboard dashboard, Texts texts)
    {
        var pending = dashboard.PendingApprovals;
        var tile = new TileState(TileIcon.Approvals, pending.Count, texts.Approvals, pending.Count > 0);
        if (pending.Count == 0)
        {
            return ([], tile);
        }

        // The id is part of every parameter, so a press always answers the request that was shown.
        var request = pending[0];
        string P(string action) => action + Separator + request.Id;
        var items = new List<FolderItem>
        {
            new(P("info"), null, Approval: request),
            new(P("allow"), null, texts.AllowOnce, TileIcon.Allow),
        };
        if (request.CanRemember)
        {
            items.Add(new(P("session"), null, texts.AllowSession, TileIcon.AllowSession));
            items.Add(new(P("always"), null, texts.AllowAlways, TileIcon.AllowAlways));
        }

        items.Add(new(P("deny"), null, texts.Deny, TileIcon.Deny));
        items.Add(new(P("ask"), null, texts.DecideInClaude, TileIcon.AskInClaude));
        return (items, tile);
    }

    internal override void OnItemPressed(FolderItem item)
    {
        var split = item.Parameter.IndexOf(Separator, StringComparison.Ordinal);
        if (split < 0)
        {
            return;
        }

        var action = item.Parameter[..split];
        var requestId = item.Parameter[(split + Separator.Length)..];
        ApprovalDecision? decision = action switch
        {
            "allow" => ApprovalDecision.Allow,
            "session" => ApprovalDecision.AllowSession,
            "always" => ApprovalDecision.AllowAlways,
            "deny" => ApprovalDecision.Deny,
            "ask" => ApprovalDecision.Ask,
            _ => null,
        };

        if (decision is { } d)
        {
            this.Owner.DecideApproval(requestId, d);
        }
        else if (item.Approval is { } request)
        {
            this.Owner.OpenSession(request.SessionId);
        }
    }
}
