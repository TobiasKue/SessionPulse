using SessionPulse.Core.Approvals;
using SessionPulse.Core.Config;
using SessionPulse.Core.Desktop;
using SessionPulse.Core.Platform;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Scheduled;
using SessionPulse.Core.Sessions;
using SessionPulse.Core.Status;
using SessionPulse.Core.Usage;
using static SessionPulse.Core.Tests.StatusFiles;

namespace SessionPulse.Core.Tests;

public sealed class DashboardTests : IDisposable
{
    private const long Now = 1_000_000;

    private readonly TempDir dir = new();
    private readonly ManualTime time = new(Now);
    private readonly RecordingPlatform platform = new();
    private readonly Dashboard dashboard;
    private SessionPulseConfig config = SessionPulseConfig.Default;

    public DashboardTests()
    {
        var store = new SessionStore(this.dir.Combine("status"), this.time);
        var acks = new AckStore(this.dir.Combine("data", "acked.json"));
        var desktop = new DesktopSessionIndex(this.dir.Combine("Claude-Data"), this.time);
        var reads = new ScheduledReadStore(this.dir.Combine("data", "scheduled-state.json"), this.time);
        var approvals = new ApprovalStore(this.dir.Combine("status"), this.time);
        var usage = new UsageStore(this.dir.Combine("status"));
        this.dashboard = new Dashboard(store, acks, desktop, this.time, () => this.config, Texts.English, slotCount: 3, reads, approvals, usage);
    }

    public void Dispose() => this.dir.Dispose();

    /// <summary>Writes a status file; stamps it as just updated so the 24 h cleanup leaves it alone.</summary>
    private void Write(SessionStatus s) => StatusFiles.Write(this.dir.Combine("status"), s with { Updated = Now });

    private IReadOnlyList<int> TickLater()
    {
        // Past the poll interval, so the store re-reads the files.
        this.time.Advance(TimeSpan.FromSeconds(6));
        return this.dashboard.Tick();
    }

    private void WriteDesktop(string hookId, string localId, string title, bool archived = false, string? taskId = null, long created = Now)
    {
        var folder = this.dir.Combine("Claude-Data", "claude_x", taskId is null ? "claude-code-sessions" : "local-agent-mode-sessions", "a", "o");
        Directory.CreateDirectory(folder);
        var task = taskId is null ? string.Empty : $$""","scheduledTaskId":"{{taskId}}" """;
        File.WriteAllText(
            Path.Combine(folder, localId + ".json"),
            $$"""{"sessionId":"{{localId}}","cliSessionId":"{{hookId}}","title":"{{title}}","isArchived":{{(archived ? "true" : "false")}},"createdAt":"{{DateTimeOffset.FromUnixTimeSeconds(created):O}}"{{task}}}""");
    }

    [Fact]
    public void BuildsKeyModels()
    {
        this.Write(Make("w", SessionState.Working, turnStarted: Now - 90, activity: Now - 5, detail: StatusDetail.Tool, tool: "Bash", tasksDone: 1, tasksTotal: 4));
        this.Write(Make("q", SessionState.Waiting, turnStarted: Now - 30, activity: Now - 30, detail: StatusDetail.Question, label: "Ask me"));
        this.Write(Make("d", SessionState.Done, turnStarted: Now - 600, activity: Now - 480, source: SessionSource.Cowork, label: "", detail: StatusDetail.Done));

        var changed = this.dashboard.Tick();

        Assert.Equal([1, 2, 3], changed);
        var keys = this.dashboard.Keys;
        Assert.Equal(new KeyModel(1, KeyState.Waiting, "q", SessionSource.Code, "Ask me", "Question", "30s", 0, 0), keys[0]);
        Assert.Equal(new KeyModel(2, KeyState.Working, "w", SessionSource.Code, "demo", "Bash", "1m", 1, 4), keys[1]);
        Assert.Equal(new KeyModel(3, KeyState.Done, "d", SessionSource.Cowork, "Cowork", "Done", "2m", 0, 0), keys[2]);
        Assert.Equal(0.25, keys[1].Progress);
        Assert.Equal(new SessionCounts(1, 1, 1), this.dashboard.Counts);

        Assert.Empty(this.dashboard.Tick());
    }

    [Fact]
    public void WorkingWithoutActivityBecomesStuck()
    {
        this.config = this.config with { StaleMinutes = 10 };
        this.Write(Make("w", SessionState.Working, activity: Now - 601));

        this.dashboard.Tick();

        Assert.Equal(KeyState.Stuck, this.dashboard.Keys[0].State);
        Assert.Equal("Stuck?", this.dashboard.Keys[0].Detail);
    }

    [Fact]
    public void HidesUnusedAndArchivedSessions()
    {
        this.Write(Make("opened-only", SessionState.Ended, prompts: 0));
        this.Write(Make("idle-new", SessionState.Idle, prompts: 0));
        this.Write(Make("archived", SessionState.Done));
        this.Write(Make("visible", SessionState.Idle, detail: StatusDetail.Interrupted));
        this.WriteDesktop("archived", "local_a", "Old", archived: true);

        this.dashboard.Tick();

        Assert.Equal(["visible", null, null], this.dashboard.Keys.Select(k => k.SessionId));
        Assert.Equal(KeyState.Idle, this.dashboard.Keys[0].State);
    }

    [Fact]
    public void DesktopTitleWinsOverLabel()
    {
        this.Write(Make("s", label: "folder"));
        this.WriteDesktop("s", "local_s", "Nice title");

        this.dashboard.Tick();

        Assert.Equal("Nice title", this.dashboard.Keys[0].Title);
    }

    [Fact]
    public void TranscriptTitleWinsOverLabelButNotOverDesktopTitle()
    {
        this.Write(Make("cli", label: "folder", title: "Fix the login", host: "cli"));
        this.Write(Make("app", label: "folder", title: "From transcript", host: SessionStatus.DesktopHost, activity: 900));
        this.WriteDesktop("app", "local_app", "From Claude Desktop");

        this.dashboard.Tick();

        Assert.Equal("Fix the login", this.dashboard.Keys.Single(k => k.SessionId == "cli").Title);
        Assert.Equal("From Claude Desktop", this.dashboard.Keys.Single(k => k.SessionId == "app").Title);
    }

    [Fact]
    public void PressOnCliSessionShowsItsTerminalNotClaudeDesktop()
    {
        this.Write(Make("cli", SessionState.Done, host: "cli", pid: 4242, title: "Fix the login"));
        this.dashboard.Tick();

        Assert.True(this.dashboard.PressKey(1, this.platform, this.platform));

        Assert.Equal([(4242, "Fix the login", false)], this.platform.Terminals);
        Assert.Empty(this.platform.Opened);
        Assert.Equal(0, this.platform.Activations);
        Assert.Equal([1], this.dashboard.Tick());   // acknowledged like any finished session
    }

    [Fact]
    public void PressOnWslSessionLooksForItsTabByTitleOnly()
    {
        // The pid would be a Linux one; the hook doesn't write it for WSL, but be safe with old files.
        this.Write(Make("wsl", SessionState.Working, host: "cli", pid: 77, wsl: "Ubuntu", title: "Casual test chat"));
        this.dashboard.Tick();

        Assert.True(this.dashboard.PressKey(1, this.platform, this.platform));

        Assert.Equal([(0, "Casual test chat", true)], this.platform.Terminals);
        Assert.Empty(this.platform.Opened);
    }

    [Fact]
    public void PressOnWslSessionWithoutTitleStillLooksForATerminal()
    {
        // The title comes with the first Stop; a long first turn has none yet.
        this.Write(Make("wsl", SessionState.Working, host: "cli", wsl: "Ubuntu"));
        this.dashboard.Tick();

        Assert.True(this.dashboard.PressKey(1, this.platform, this.platform));

        Assert.Equal([(0, (string?)null, true)], this.platform.Terminals);
        Assert.Empty(this.platform.Opened);
    }

    [Fact]
    public void PressOnDesktopSessionNotYetIndexedOpensTheApp()
    {
        this.Write(Make("new", SessionState.Working, host: SessionStatus.DesktopHost, pid: 21616));
        this.dashboard.Tick();

        this.dashboard.PressKey(1, this.platform, this.platform);

        Assert.Equal([DeepLinks.App], this.platform.Opened);
        Assert.Empty(this.platform.Terminals);
    }

    [Fact]
    public void PressOpensSessionAndAcknowledgesFinished()
    {
        this.Write(Make("d", SessionState.Done, prompts: 2, tools: 3));
        this.WriteDesktop("d", "local_d", "Done task");
        this.dashboard.Tick();

        Assert.True(this.dashboard.PressKey(1, this.platform, this.platform));

        Assert.Equal(["claude://code/continue?session=local_d"], this.platform.Opened);
        Assert.Equal(1, this.platform.Activations);
        Assert.Equal([1], this.dashboard.Tick());
        Assert.Equal(KeyState.Empty, this.dashboard.Keys[0].State);

        // New activity brings it back.
        this.Write(Make("d", SessionState.Done, prompts: 3, tools: 3));
        this.TickLater();
        Assert.Equal("d", this.dashboard.Keys[0].SessionId);
    }

    [Fact]
    public void PressOnRunningSessionDoesNotAcknowledge()
    {
        this.Write(Make("w", SessionState.Working));
        this.dashboard.Tick();

        this.dashboard.PressKey(1, this.platform, this.platform);

        Assert.Equal([DeepLinks.App], this.platform.Opened);
        Assert.Equal("w", this.dashboard.Keys[0].SessionId);
    }

    [Fact]
    public void PressOnEmptyKeyDoesNothing()
    {
        this.dashboard.Tick();

        Assert.False(this.dashboard.PressKey(2, this.platform, this.platform));
        Assert.False(this.dashboard.PressKey(99, this.platform, this.platform));
        Assert.Empty(this.platform.Opened);
    }

    [Fact]
    public void AcknowledgeAllHidesFinishedSessions()
    {
        this.Write(Make("d1", SessionState.Done));
        this.Write(Make("d2", SessionState.Ended));
        this.Write(Make("w", SessionState.Working));
        this.dashboard.Tick();

        Assert.Equal(2, this.dashboard.AcknowledgeAllFinished());
        this.dashboard.Tick();

        Assert.Equal(["w", null, null], this.dashboard.Keys.Select(k => k.SessionId));
        Assert.Equal(new SessionCounts(1, 0, 0), this.dashboard.Counts);
    }

    [Fact]
    public void GroupKeysListVisibleSessionsMostRecentFirst()
    {
        this.Write(Make("w-old", SessionState.Working, activity: Now - 50));
        this.Write(Make("w-new", SessionState.Working, activity: Now - 10));
        this.Write(Make("w-stuck", SessionState.Working, activity: Now - 3600));
        this.Write(Make("q", SessionState.Waiting));
        this.Write(Make("d", SessionState.Done));
        this.Write(Make("e", SessionState.Ended));
        this.dashboard.Tick();

        var working = this.dashboard.GroupKeys(SessionGroup.Working, 8);
        Assert.Equal(["w-new", "w-old", "w-stuck"], working.Select(k => k.SessionId));
        Assert.Equal([1, 2, 3], working.Select(k => k.Number));
        Assert.Equal(KeyState.Stuck, working[2].State);
        Assert.Equal(["w-new", "w-old"], this.dashboard.GroupKeys(SessionGroup.Working, 2).Select(k => k.SessionId));
        Assert.Equal(2, this.dashboard.GroupKeys(SessionGroup.Done, 8).Count);
        Assert.Equal(3, this.dashboard.Counts[SessionGroup.Working]);
        Assert.Equal(1, this.dashboard.Counts[SessionGroup.Waiting]);
        Assert.Equal(2, this.dashboard.Counts[SessionGroup.Done]);
    }

    [Fact]
    public void PressSessionOpensAndAcknowledges()
    {
        this.Write(Make("d", SessionState.Done));
        this.WriteDesktop("d", "local_d", "Done task");
        this.dashboard.Tick();

        Assert.True(this.dashboard.PressSession("d", this.platform, this.platform));
        Assert.False(this.dashboard.PressSession("missing", this.platform, this.platform));

        Assert.Equal(["claude://code/continue?session=local_d"], this.platform.Opened);
        this.dashboard.Tick();
        Assert.Empty(this.dashboard.GroupKeys(SessionGroup.Done, 8));
        Assert.Equal(0, this.dashboard.Counts.Done);
    }

    [Fact]
    public void ScheduledRunsCanBeOpenedAndMarkedRead()
    {
        this.WriteDesktop("cli-1", "local_1", "Morning digest", taskId: "t1", created: Now - 600);
        this.Write(Make("cli-1", SessionState.Done, sched: "digest"));
        this.dashboard.Tick();

        Assert.Equal(1, this.dashboard.UnreadScheduledRuns);
        var (runKey, model) = Assert.Single(this.dashboard.ScheduledKeys(8));
        Assert.Equal("local_1", runKey);
        Assert.Equal("Morning digest", model.Title);
        Assert.True(model.Unread);
        Assert.Equal("10m", model.Elapsed);

        Assert.True(this.dashboard.PressScheduledRun("local_1", this.platform, this.platform, this.platform));

        Assert.Equal(["claude://claude.ai/cowork/local_1"], this.platform.Opened);
        Assert.Equal(["local_1"], this.platform.Scrolled);
        Assert.Equal(0, this.dashboard.UnreadScheduledRuns);
        Assert.False(this.dashboard.ScheduledKeys(8)[0].Model.Unread);

        // Opening it also acknowledged the finished session.
        this.dashboard.Tick();
        Assert.Equal(0, this.dashboard.Counts.Done);
    }

    [Fact]
    public void CompactingAndFailedStates()
    {
        this.Write(Make("c", SessionState.Working, detail: StatusDetail.Compacting, activity: Now));
        this.Write(Make("l", SessionState.Done, detail: StatusDetail.Limit));
        this.Write(Make("e", SessionState.Done, detail: StatusDetail.Error));
        this.dashboard.Tick();

        var byId = this.dashboard.Keys.ToDictionary(k => k.SessionId!);
        Assert.Equal((KeyState.Compacting, "Compacting"), (byId["c"].State, byId["c"].Detail));
        Assert.Equal((KeyState.Failed, "Limit"), (byId["l"].State, byId["l"].Detail));
        Assert.Equal((KeyState.Failed, "Error"), (byId["e"].State, byId["e"].Detail));
        Assert.Equal(new SessionCounts(1, 0, 2), this.dashboard.Counts);
    }

    [Fact]
    public void NextWaitingPrefersLongestWaitThenStuckThenNewestDone()
    {
        this.config = this.config with { StaleMinutes = 10 };
        this.Write(Make("done-old", SessionState.Done, activity: Now - 500));
        this.Write(Make("done-new", SessionState.Done, activity: Now - 100));
        this.Write(Make("stuck", SessionState.Working, activity: Now - 3600));
        this.Write(Make("wait-new", SessionState.Waiting, activity: Now - 10));
        this.Write(Make("wait-old", SessionState.Waiting, activity: Now - 60));
        this.WriteDesktop("wait-old", "local_w", "W");
        this.dashboard.Tick();

        Assert.Equal("wait-old", this.dashboard.NextWaiting()!.Id);
        Assert.True(this.dashboard.PressNextWaiting(this.platform, this.platform));
        Assert.Equal(["claude://code/continue?session=local_w"], this.platform.Opened);

        this.Write(Make("wait-old", SessionState.Working, activity: Now));
        this.Write(Make("wait-new", SessionState.Working, activity: Now));
        this.TickLater();
        Assert.Equal("stuck", this.dashboard.NextWaiting()!.Id);

        this.Write(Make("stuck", SessionState.Working, activity: this.time.UnixNow));
        this.TickLater();
        Assert.Equal("done-new", this.dashboard.NextWaiting()!.Id);
    }

    [Fact]
    public void NextWaitingIsNullWhenNothingNeedsYou()
    {
        this.Write(Make("w", SessionState.Working, activity: Now));
        this.dashboard.Tick();

        Assert.Null(this.dashboard.NextWaiting());
        Assert.False(this.dashboard.PressNextWaiting(this.platform, this.platform));
    }

    [Fact]
    public void ApprovalsAreListedAndAnswered()
    {
        var folder = this.dir.Combine("status", "approvals");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "r1.json"), $$"""{"v":1,"id":"r1","session":"s","tool":"Bash","preview":"ls","risk":"normal","remember":false,"created":{{Now}},"expires":{{Now + 30}}}""");
        this.dashboard.Tick();

        Assert.Equal("r1", Assert.Single(this.dashboard.PendingApprovals).Id);
        Assert.True(this.dashboard.DecideApproval("r1", ApprovalDecision.Deny));
        Assert.Equal("deny", File.ReadAllText(Path.Combine(folder, "r1.decision")).Trim());
    }

    [Fact]
    public void RaisesHapticEventsOnlyForChanges()
    {
        this.Write(Make("a", SessionState.Waiting));
        this.Write(Make("b", SessionState.Working, activity: Now));
        this.dashboard.Tick();
        Assert.Empty(this.dashboard.TakeEvents());

        this.Write(Make("b", SessionState.Waiting, activity: Now));
        this.TickLater();
        Assert.Equal([PulseEvent.SessionWaiting], this.dashboard.TakeEvents());
        Assert.Empty(this.dashboard.TakeEvents());

        this.Write(Make("b", SessionState.Done, activity: Now));
        this.TickLater();
        Assert.Equal([PulseEvent.SessionDone], this.dashboard.TakeEvents());

        // Done -> ended (Cowork sends SessionEnd after every answer) is not a new event.
        this.Write(Make("b", SessionState.Ended, activity: Now));
        this.TickLater();
        Assert.Empty(this.dashboard.TakeEvents());
    }

    [Fact]
    public void ApprovalEventWinsOverWaitingAndUsageAlertsOnce()
    {
        this.Write(Make("a", SessionState.Working, activity: Now));
        this.dashboard.Tick();

        var folder = this.dir.Combine("status", "approvals");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "r1.json"), $$"""{"v":1,"id":"r1","session":"a","tool":"Bash","preview":"ls","risk":"normal","remember":false,"created":{{Now}},"expires":{{Now + 300}}}""");
        this.Write(Make("a", SessionState.Waiting, activity: Now));
        this.TickLater();
        Assert.Equal([PulseEvent.ApprovalRequested], this.dashboard.TakeEvents());

        File.WriteAllText(this.dir.Combine("status", "usage.json"), """{"v":1,"updated":1,"fiveHour":{"pct":91,"resetsAt":0}}""");
        this.TickLater();
        Assert.Equal([PulseEvent.UsageHigh], this.dashboard.TakeEvents());
        Assert.Equal(91, this.dashboard.Usage!.FiveHour!.Value.Percent);

        File.WriteAllText(this.dir.Combine("status", "usage.json"), """{"v":1,"updated":2,"fiveHour":{"pct":95,"resetsAt":0}}""");
        File.SetLastWriteTimeUtc(this.dir.Combine("status", "usage.json"), DateTime.UtcNow.AddMinutes(1));
        this.TickLater();
        Assert.Empty(this.dashboard.TakeEvents());
    }

    private sealed class RecordingPlatform : IUriLauncher, IWindowActivator, IScrollToTop
    {
        public List<string> Opened { get; } = [];

        public List<string> Scrolled { get; } = [];

        public void ScrollToTop(string localSessionId) => this.Scrolled.Add(localSessionId);

        public int Activations { get; private set; }

        public bool Open(string uri)
        {
            this.Opened.Add(uri);
            return true;
        }

        public List<(int ProcessId, string? Title, bool Wsl)> Terminals { get; } = [];

        public bool BringClaudeToFront()
        {
            this.Activations++;
            return true;
        }

        public bool BringTerminalToFront(int processId, string? title, bool wsl)
        {
            this.Terminals.Add((processId, title, wsl));
            return true;
        }
    }
}
