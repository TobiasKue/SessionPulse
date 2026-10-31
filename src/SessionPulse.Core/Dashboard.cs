using SessionPulse.Core.Approvals;
using SessionPulse.Core.Config;
using SessionPulse.Core.Desktop;
using SessionPulse.Core.Platform;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Scheduled;
using SessionPulse.Core.Sessions;
using SessionPulse.Core.Status;
using SessionPulse.Core.Usage;

namespace SessionPulse.Core;

/// <summary>Number of sessions per state, for the counter tiles.</summary>
public readonly record struct SessionCounts(int Working, int Waiting, int Done)
{
    public int this[SessionGroup group] => group switch
    {
        SessionGroup.Working => this.Working,
        SessionGroup.Waiting => this.Waiting,
        _ => this.Done,
    };
}

/// <summary>Moments worth a haptic nudge (Logi event names in <see cref="Dashboard.EventName"/>).</summary>
public enum PulseEvent
{
    SessionWaiting,
    ApprovalRequested,
    SessionDone,
    UsageHigh,
}

/// <summary>The three counter folders. "Working" includes sessions that look stuck.</summary>
public enum SessionGroup
{
    Working,
    Waiting,
    Done,
}

/// <summary>
/// The plugin's brain, free of any Logi or Windows dependency: reads status files and Claude Desktop
/// metadata, decides which session gets which key, builds the key models and handles key presses.
/// Not thread-safe; the plugin calls it from one place (its tick) and under its own lock.
/// </summary>
public sealed class Dashboard
{
    private readonly SessionStore store;
    private readonly AckStore acks;
    private readonly DesktopSessionIndex desktop;
    private readonly TimeProvider time;
    private readonly Func<SessionPulseConfig> config;
    private readonly Texts texts;
    private readonly SlotAssigner slots;
    private readonly ScheduledReadStore? scheduledReads;
    private readonly ApprovalStore? approvals;
    private readonly UsageStore? usage;
    private readonly List<PulseEvent> events = [];
    private Dictionary<string, SessionState>? lastStates;
    private HashSet<string> lastApprovalIds = new(StringComparer.Ordinal);
    private bool usageAlerted;
    private IReadOnlyList<ScheduledRun> scheduledRuns = [];
    private KeyModel[] keys;
    private bool forceRebuild = true;
    private SessionPulseConfig lastConfig = SessionPulseConfig.Default;

    public Dashboard(
        SessionStore store,
        AckStore acks,
        DesktopSessionIndex desktop,
        TimeProvider time,
        Func<SessionPulseConfig> config,
        Texts texts,
        int slotCount,
        ScheduledReadStore? scheduledReads = null,
        ApprovalStore? approvals = null,
        UsageStore? usage = null)
    {
        this.scheduledReads = scheduledReads;
        this.approvals = approvals;
        this.usage = usage;
        this.store = store;
        this.acks = acks;
        this.desktop = desktop;
        this.time = time;
        this.config = config;
        this.texts = texts;
        this.slots = new SlotAssigner(slotCount);
        this.keys = Enumerable.Range(1, slotCount).Select(KeyModel.Empty).ToArray();
    }

    public IReadOnlyList<KeyModel> Keys => this.keys;

    public SessionCounts Counts { get; private set; }

    /// <summary>Permission requests waiting for a keypad decision, oldest first.</summary>
    public IReadOnlyList<PendingApproval> PendingApprovals { get; private set; } = [];

    /// <summary>Subscription usage from the status line helper, or null.</summary>
    public UsageSnapshot? Usage { get; private set; }

    /// <summary>Number of scheduled tasks whose newest run nobody has looked at yet.</summary>
    public int UnreadScheduledRuns { get; private set; }

    /// <summary>
    /// Reads new data if due and rebuilds the key models. Returns the numbers (1-based) of the keys
    /// whose model changed (all keys on the first call).
    /// </summary>
    public IReadOnlyList<int> Tick()
    {
        var cfg = this.config();
        this.lastConfig = cfg;
        var sessionsChanged = this.store.RefreshIfDue(cfg);
        this.desktop.RescanIfDue();
        if (sessionsChanged)
        {
            this.acks.Prune(this.store.Sessions.Keys);
        }

        var now = this.time.GetUtcNow().ToUnixTimeSeconds();
        var sessions = this.store.Sessions;
        var visible = new List<SlotCandidate>();
        int working = 0, waiting = 0, done = 0;
        foreach (var s in sessions.Values)
        {
            if (!this.IsVisible(s))
            {
                continue;
            }

            visible.Add(new SlotCandidate(s.Id, Rank(s), s.Activity > 0 ? s.Activity : s.Updated));
            switch (s.State)
            {
                case SessionState.Working: working++; break;
                case SessionState.Waiting: waiting++; break;
                case SessionState.Done or SessionState.Ended: done++; break;
            }
        }

        this.Counts = new SessionCounts(working, waiting, done);
        this.UpdateScheduledRuns(sessionsChanged);
        this.PendingApprovals = this.approvals?.Pending() ?? [];
        this.Usage = this.usage?.Current;
        this.DetectEvents(sessions, now);
        this.slots.Update(visible);

        var changed = new List<int>();
        for (var i = 0; i < this.keys.Length; i++)
        {
            var id = this.slots.Slots[i];
            var model = id is not null && sessions.TryGetValue(id, out var status)
                ? this.BuildModel(i + 1, status, now, cfg)
                : KeyModel.Empty(i + 1);
            if (this.forceRebuild || model != this.keys[i])
            {
                this.keys[i] = model;
                changed.Add(i + 1);
            }
        }

        this.forceRebuild = false;
        return changed;
    }

    public SessionStatus? SessionOnKey(int number)
    {
        var id = number >= 1 && number <= this.slots.Count ? this.slots.Slots[number - 1] : null;
        return id is not null ? this.store.Sessions.GetValueOrDefault(id) : null;
    }

    /// <summary>
    /// Handles a press on a session key: opens the session in Claude Desktop and acknowledges it if it
    /// is finished. Returns false if the key is empty.
    /// </summary>
    public bool PressKey(int number, IUriLauncher launcher, IWindowActivator activator)
    {
        var status = this.SessionOnKey(number);
        if (status is null)
        {
            return false;
        }

        this.Open(status, launcher, activator);
        this.acks.Ack(status);
        return true;
    }

    /// <summary>
    /// The session that most needs you: waiting the longest, then possibly stuck, then the most recently
    /// finished one that nobody has looked at.
    /// </summary>
    public SessionStatus? NextWaiting()
    {
        var now = this.time.GetUtcNow().ToUnixTimeSeconds();
        var visible = this.store.Sessions.Values.Where(this.IsVisible).ToList();
        return visible.Where(s => s.State == SessionState.Waiting).OrderBy(s => s.Activity).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault()
            ?? visible.Where(s => this.IsStuck(s, now)).OrderBy(s => s.Activity).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault()
            ?? visible.Where(s => s.IsFinished).OrderByDescending(s => s.Activity).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Opens <see cref="NextWaiting"/> (acknowledging it if finished). False if there is none.</summary>
    public bool PressNextWaiting(IUriLauncher launcher, IWindowActivator activator) =>
        this.NextWaiting() is { } next && this.PressSession(next.Id, launcher, activator);

    /// <summary>Answers a held permission request; false if it is gone (answered, timed out).</summary>
    public bool DecideApproval(string requestId, ApprovalDecision decision)
    {
        var done = this.approvals?.Decide(requestId, decision) ?? false;
        this.PendingApprovals = this.approvals?.Pending() ?? [];
        return done;
    }

    /// <summary>Haptic moments since the last call (none for the state found at start-up).</summary>
    public IReadOnlyList<PulseEvent> TakeEvents()
    {
        var taken = this.events.ToList();
        this.events.Clear();
        return taken;
    }

    public static string EventName(PulseEvent e) => e switch
    {
        PulseEvent.SessionWaiting => "sessionWaiting",
        PulseEvent.ApprovalRequested => "approvalRequested",
        PulseEvent.SessionDone => "sessionDone",
        _ => "usageHigh",
    };

    /// <summary>
    /// Opens a session: in Claude Desktop by deep link if Claude Desktop knows it, the terminal of a CLI
    /// session, otherwise just the app. A CLI session never opens Claude Desktop, which can't show it.
    /// </summary>
    public void Open(SessionStatus status, IUriLauncher launcher, IWindowActivator activator)
    {
        var known = this.desktop.Find(status.Id);
        if (known is null && status.RunsOutsideDesktop)
        {
            // A WSL session's process id would be a Linux one; its tab is found by the title, if it shows it.
            var wsl = status.Wsl is not null;
            var processId = wsl ? 0 : status.ProcessId;
            if (processId > 0 || status.Title is not null || wsl)
            {
                activator.BringTerminalToFront(processId, status.Title, wsl);
            }

            return;
        }

        launcher.Open(known?.DeepLink ?? DeepLinks.App);

        // Claude cannot take the focus itself when the deep link arrives (Windows focus rules).
        activator.BringClaudeToFront();
    }

    /// <summary>Acknowledges every finished session. Returns the number acknowledged.</summary>
    public int AcknowledgeAllFinished()
    {
        var count = 0;
        foreach (var s in this.store.Sessions.Values)
        {
            if (this.IsVisible(s) && s.IsFinished && this.acks.Ack(s))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Key models for the visible sessions of a group, most recent first, at most <paramref name="max"/>.
    /// Numbers are positions (1-based) in the folder.
    /// </summary>
    public IReadOnlyList<KeyModel> GroupKeys(SessionGroup group, int max)
    {
        var now = this.time.GetUtcNow().ToUnixTimeSeconds();
        return this.store.Sessions.Values
            .Where(s => this.IsVisible(s) && InGroup(s, group))
            .OrderByDescending(s => s.Activity > 0 ? s.Activity : s.Updated)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .Take(max)
            .Select((s, i) => this.BuildModel(i + 1, s, now, this.lastConfig))
            .ToList();
    }

    /// <summary>Like <see cref="PressKey"/>, for a session picked in a folder.</summary>
    public bool PressSession(string sessionId, IUriLauncher launcher, IWindowActivator activator)
    {
        if (!this.store.Sessions.TryGetValue(sessionId, out var status))
        {
            return false;
        }

        this.Open(status, launcher, activator);
        this.acks.Ack(status);
        return true;
    }

    /// <summary>Keys for the scheduled-tasks folder: newest run per task, unread first.</summary>
    public IReadOnlyList<(string RunKey, KeyModel Model)> ScheduledKeys(int max)
    {
        var now = this.time.GetUtcNow();
        return this.scheduledRuns.Take(max).Select((run, i) =>
        {
            var age = run.CreatedAt is { } created ? DurationFormat.Short(now - created) : string.Empty;
            var model = run.Status is { } status
                ? this.BuildModel(i + 1, status, now.ToUnixTimeSeconds(), this.lastConfig)
                : new KeyModel(i + 1, KeyState.Done, null, SessionSource.Cowork, run.Title, this.texts.Detail(StatusDetail.Done, null), age, 0, 0);
            if (model.State is KeyState.Done or KeyState.Idle)
            {
                // For finished runs, how long ago they ran says more than how long they took.
                model = model with { Elapsed = age };
            }

            return (run.RunKey, model with { Number = i + 1, Title = run.Title, Unread = run.Unread });
        }).ToList();
    }

    /// <summary>
    /// Opens a scheduled run, scrolls it to the top (Claude Desktop opens long runs at the end) and marks
    /// it read. A finished run is acknowledged as well, as it has now been seen.
    /// </summary>
    public bool PressScheduledRun(string runKey, IUriLauncher launcher, IWindowActivator activator, IScrollToTop scroller)
    {
        var run = this.scheduledRuns.FirstOrDefault(r => r.RunKey == runKey);
        if (run is null)
        {
            return false;
        }

        launcher.Open(run.Desktop?.DeepLink ?? DeepLinks.App);
        activator.BringClaudeToFront();
        if (run.Desktop is { } desktop)
        {
            scroller.ScrollToTop(desktop.LocalId);
        }

        this.scheduledReads?.MarkRead(run.RunKey);
        if (run.Status is { } status)
        {
            this.acks.Ack(status);
        }

        this.UpdateScheduledRuns(pruneReads: false);
        return true;
    }

    public static bool InGroup(SessionStatus s, SessionGroup group) => group switch
    {
        SessionGroup.Working => s.State == SessionState.Working,
        SessionGroup.Waiting => s.State == SessionState.Waiting,
        _ => s.IsFinished,
    };

    public KeyModel BuildModel(int number, SessionStatus s, long now, SessionPulseConfig cfg)
    {
        var state = s.State switch
        {
            SessionState.Working when s.Activity > 0 && now - s.Activity > cfg.StaleMinutes * 60L => KeyState.Stuck,
            SessionState.Working when s.Detail == StatusDetail.Compacting => KeyState.Compacting,
            SessionState.Working => KeyState.Working,
            SessionState.Waiting => KeyState.Waiting,
            SessionState.Done or SessionState.Ended when s.Detail is StatusDetail.Error or StatusDetail.Limit => KeyState.Failed,
            SessionState.Done or SessionState.Ended => KeyState.Done,
            _ => KeyState.Idle,
        };

        var turnStart = s.TurnStarted > 0 ? s.TurnStarted : s.Started;
        var elapsed = state switch
        {
            KeyState.Working or KeyState.Waiting or KeyState.Stuck or KeyState.Compacting => DurationFormat.Short(TimeSpan.FromSeconds(now - turnStart)),
            KeyState.Done or KeyState.Failed when s.TurnStarted > 0 && s.Activity >= s.TurnStarted => DurationFormat.Short(TimeSpan.FromSeconds(s.Activity - s.TurnStarted)),
            _ => string.Empty,
        };

        // Claude Desktop's title, else the one the hook read from the transcript (CLI sessions), else the label.
        var title = this.desktop.Find(s.Id)?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = s.Title;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = !string.IsNullOrWhiteSpace(s.Label) ? s.Label
                : s.Source == SessionSource.Cowork ? this.texts.Cowork
                : this.texts.Code;
        }

        var detail = state == KeyState.Stuck ? this.texts.Stuck : this.texts.Detail(s.Detail, s.Tool);
        return new KeyModel(number, state, s.Id, s.Source, title, detail, elapsed, s.TasksDone, s.TasksTotal);
    }

    /// <summary>
    /// Hidden: acknowledged finished sessions, archived sessions, and sessions that were only opened
    /// but never used (Cowork sends SessionStart/SessionEnd just for opening one).
    /// </summary>
    public bool IsVisible(SessionStatus s)
    {
        if (s.Prompts == 0 && s.State is not (SessionState.Working or SessionState.Waiting))
        {
            return false;
        }

        if (this.desktop.Find(s.Id)?.IsArchived == true)
        {
            return false;
        }

        return !this.acks.IsAcked(s);
    }

    private bool IsStuck(SessionStatus s, long now) =>
        s.State == SessionState.Working && s.Activity > 0 && now - s.Activity > this.lastConfig.StaleMinutes * 60L;

    private void DetectEvents(IReadOnlyDictionary<string, SessionStatus> sessions, long now)
    {
        var states = sessions.ToDictionary(kv => kv.Key, kv => kv.Value.State, StringComparer.Ordinal);
        var approvalIds = this.PendingApprovals.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var maxUsage = this.Usage?.MaxPercent(now) ?? 0;
        if (this.lastStates is null)
        {
            // First tick: remember the situation, don't buzz for what was already there.
            this.lastStates = states;
            this.lastApprovalIds = approvalIds;
            this.usageAlerted = maxUsage >= 90;
            return;
        }

        if (approvalIds.Any(id => !this.lastApprovalIds.Contains(id)))
        {
            this.events.Add(PulseEvent.ApprovalRequested);
        }
        else if (states.Any(kv => kv.Value == SessionState.Waiting && this.lastStates.GetValueOrDefault(kv.Key) != SessionState.Waiting))
        {
            this.events.Add(PulseEvent.SessionWaiting);
        }

        if (states.Any(kv => kv.Value is SessionState.Done or SessionState.Ended
            && this.lastStates.TryGetValue(kv.Key, out var before) && before is SessionState.Working or SessionState.Waiting))
        {
            this.events.Add(PulseEvent.SessionDone);
        }

        if (maxUsage >= 90 && !this.usageAlerted)
        {
            this.events.Add(PulseEvent.UsageHigh);
            this.usageAlerted = true;
        }
        else if (maxUsage < 80)
        {
            this.usageAlerted = false;
        }

        this.lastStates = states;
        this.lastApprovalIds = approvalIds;
    }

    private void UpdateScheduledRuns(bool pruneReads)
    {
        if (this.scheduledReads is null)
        {
            return;
        }

        var desktopSessions = this.desktop.Sessions.Values.ToList();
        var statuses = this.store.Sessions;
        if (pruneReads && desktopSessions.Count > 0)
        {
            // Keep marks of every known run, not only the newest per task: when the newest run gets archived,
            // the one before it must not turn unread again.
            this.scheduledReads.Prune(desktopSessions.Select(d => d.LocalId).Concat(statuses.Keys.Select(id => "hook_" + id)));
        }

        this.scheduledRuns = ScheduledRuns.Collect(desktopSessions, statuses, this.scheduledReads, this.texts.Scheduled);
        this.UnreadScheduledRuns = this.scheduledRuns.Count(r => r.Unread);
    }

    private static int Rank(SessionStatus s) => s.State switch
    {
        SessionState.Waiting => 0,
        SessionState.Working => 1,
        SessionState.Done or SessionState.Ended => 2,
        _ => 3,
    };
}
