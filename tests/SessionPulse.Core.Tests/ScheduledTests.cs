using SessionPulse.Core.Desktop;
using SessionPulse.Core.Scheduled;
using SessionPulse.Core.Status;
using static SessionPulse.Core.Tests.StatusFiles;

namespace SessionPulse.Core.Tests;

public sealed class ScheduledTests : IDisposable
{
    // 2026-10-23 12:00 UTC
    private const long Noon = 1_792_756_800;

    private readonly TempDir dir = new();
    private readonly ManualTime time = new(Noon);

    public void Dispose() => this.dir.Dispose();

    private ScheduledReadStore NewStore() => new(this.dir.Combine("data", "scheduled-state.json"), this.time);

    private static DesktopSession Run(string localId, string? cli, string? taskId, string? title, long created, bool archived = false) =>
        new(DesktopSessionKind.Cowork, localId, cli, title, archived, DateTimeOffset.FromUnixTimeSeconds(created), taskId);

    private static Dictionary<string, SessionStatus> Statuses(params SessionStatus[] s) => s.ToDictionary(x => x.Id);

    [Fact]
    public void RunsBeforeTheFirstDayCountAsRead()
    {
        var store = this.NewStore();
        var startOfDay = new DateTimeOffset(this.time.GetLocalNow().Date, this.time.GetLocalNow().Offset);

        Assert.Equal(startOfDay, store.ReadBefore);
        Assert.True(store.IsRead("old", startOfDay.AddSeconds(-1)));
        Assert.False(store.IsRead("today", startOfDay.AddSeconds(1)));
        Assert.False(store.IsRead("unknown-time", null));
    }

    [Fact]
    public void ReadMarksPersistAndPrune()
    {
        var store = this.NewStore();
        store.MarkRead("a");
        store.MarkRead("b");
        store.Prune(["a"]);

        // A later start keeps the original cut-off day.
        this.time.Advance(TimeSpan.FromDays(3));
        var reloaded = this.NewStore();

        Assert.True(reloaded.IsRead("a", this.time.GetUtcNow()));
        Assert.False(reloaded.IsRead("b", this.time.GetUtcNow()));
        Assert.Equal(store.ReadBefore, reloaded.ReadBefore);
    }

    [Fact]
    public void NewestRunPerTaskUnreadFirst()
    {
        var store = this.NewStore();
        var desktop = new[]
        {
            Run("local_a1", "cli-a1", "task-a", "Digest Monday", Noon - 3600),
            Run("local_a2", "cli-a2", "task-a", "Digest Tuesday", Noon - 60),
            Run("local_b1", "cli-b1", "task-b", "Backup check", Noon - 30),
            Run("local_c1", "cli-c1", "task-c", "Old report", Noon - 86400 * 2),
            Run("local_x", "cli-x", null, "Not scheduled", Noon),
        };
        store.MarkRead("local_b1");

        var runs = ScheduledRuns.Collect(desktop, Statuses(), store, "Scheduled");

        Assert.Equal(["local_a2", "local_b1", "local_c1"], runs.Select(r => r.RunKey));
        Assert.Equal([true, false, false], runs.Select(r => r.Unread));
        Assert.Equal("Digest Tuesday", runs[0].Title);
    }

    [Fact]
    public void RawPromptTitleFallsBackToEarlierRunThenTaskName()
    {
        var store = this.NewStore();
        var desktop = new[]
        {
            Run("local_1", "cli-1", "task-a", "Weekly digest", Noon - 7200),
            Run("local_2", "cli-2", "task-a", "<scheduled-task name='digest'>", Noon - 60),
            Run("local_3", "cli-3", "task-b", null, Noon - 30),
        };
        var statuses = Statuses(Make("cli-3", SessionState.Done, sched: "Inbox zero"));

        var runs = ScheduledRuns.Collect(desktop, statuses, store, "Scheduled");

        Assert.Equal("Weekly digest", runs.Single(r => r.RunKey == "local_2").Title);
        Assert.Equal("Inbox zero", runs.Single(r => r.RunKey == "local_3").Title);
    }

    [Fact]
    public void HookOnlyRunsJoinTheirTaskByName()
    {
        var store = this.NewStore();

        // local_2's metadata lacks scheduledTaskId (beyond the 64 KB read limit); its status file knows the name.
        var desktop = new[]
        {
            Run("local_1", "cli-1", "task-a", "Digest 1", Noon - 7200),
            Run("local_2", "cli-2", null, "Digest 2", Noon - 60),
        };
        var statuses = Statuses(
            Make("cli-1", SessionState.Done, sched: "digest"),
            Make("cli-2", SessionState.Done, sched: "digest"),
            Make("cli-9", SessionState.Done, sched: "other", started: Noon - 10));

        var runs = ScheduledRuns.Collect(desktop, statuses, store, "Scheduled");

        Assert.Equal(2, runs.Count);
        Assert.Equal("local_2", runs.Single(r => r.TaskKey == "id:task-a").RunKey);
        var other = runs.Single(r => r.TaskKey == "name:other");
        Assert.Equal("hook_cli-9", other.RunKey);
        Assert.Equal("other", other.Title);
        Assert.Null(other.Desktop);
    }

    [Fact]
    public void ArchivedRunsAreSkippedAndWorkingRunsAreNotUnread()
    {
        var store = this.NewStore();
        var desktop = new[]
        {
            Run("local_1", "cli-1", "task-a", "Older", Noon - 7200),
            Run("local_2", "cli-2", "task-a", "Archived", Noon - 60, archived: true),
            Run("local_3", "cli-3", "task-b", "Running", Noon - 30),
        };
        var statuses = Statuses(Make("cli-3", SessionState.Working, sched: "b"));

        var runs = ScheduledRuns.Collect(desktop, statuses, store, "Scheduled");

        Assert.Equal("local_1", runs.Single(r => r.TaskKey == "id:task-a").RunKey);
        Assert.False(runs.Single(r => r.TaskKey == "id:task-b").Unread);
    }
}
