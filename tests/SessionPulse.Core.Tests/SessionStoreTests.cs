using SessionPulse.Core.Config;
using SessionPulse.Core.Sessions;
using SessionPulse.Core.Status;
using static SessionPulse.Core.Tests.StatusFiles;

namespace SessionPulse.Core.Tests;

public class SessionStoreTests
{
    private static readonly SessionPulseConfig Config = SessionPulseConfig.Default;

    [Fact]
    public void MissingDirectoryMeansNoSessions()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Combine("nothing"), new ManualTime(1000));

        Assert.False(store.Refresh(Config));
        Assert.Empty(store.Sessions);
        Assert.False(store.HookSeen);
    }

    [Fact]
    public void ReadsSessionsAndReportsChanges()
    {
        using var dir = new TempDir();
        var time = new ManualTime(2000);
        var store = new SessionStore(dir.Path, time);
        Write(dir.Path, Make("a"));
        Write(dir.Path, Make("b", SessionState.Done));

        Assert.True(store.Refresh(Config));
        Assert.Equal(["a", "b"], store.Sessions.Keys.Order());
        Assert.True(store.HookSeen);

        Assert.False(store.Refresh(Config));

        Write(dir.Path, Make("a", tools: 3));
        Assert.True(store.Refresh(Config));
        Assert.Equal(3, store.Sessions["a"].Tools);
    }

    [Fact]
    public void BrokenFileKeepsLastGoodState()
    {
        using var dir = new TempDir();
        var logged = new List<string>();
        var store = new SessionStore(dir.Path, new ManualTime(2000), logged.Add);
        Write(dir.Path, Make("a", tools: 7));
        store.Refresh(Config);

        File.WriteAllText(dir.Combine("sessions", "a.json"), """{"v":2,"id":"a","sta""");
        store.Refresh(Config);
        store.Refresh(Config);

        Assert.Equal(7, store.Sessions["a"].Tools);
        Assert.Single(logged);
    }

    [Fact]
    public void IgnoresOtherVersionsAndTempFiles()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.Path, new ManualTime(2000));
        Directory.CreateDirectory(dir.Combine("sessions"));
        File.WriteAllText(dir.Combine("sessions", "old.json"), """{"v":1,"id":"old","state":"done"}""");
        File.WriteAllText(dir.Combine("sessions", ".a.123.tmp"), "partial");

        store.Refresh(Config);

        Assert.Empty(store.Sessions);
        Assert.True(File.Exists(dir.Combine("sessions", ".a.123.tmp")), "fresh temp file must survive");
    }

    [Fact]
    public void DeletesOutdatedFiles()
    {
        using var dir = new TempDir();
        var time = new ManualTime(100_000);
        var store = new SessionStore(dir.Path, time);
        Write(dir.Path, Make("old", SessionState.Done, updated: 100_000 - (25 * 3600)));
        Write(dir.Path, Make("new", SessionState.Done, updated: 100_000 - (23 * 3600)));

        store.Refresh(Config);

        Assert.Equal(["new"], store.Sessions.Keys);
        Assert.False(File.Exists(dir.Combine("sessions", "old.json")));
    }

    [Fact]
    public void RefreshIfDuePollsEveryFiveSeconds()
    {
        using var dir = new TempDir();
        var time = new ManualTime(2000);
        var store = new SessionStore(dir.Path, time);
        store.RefreshIfDue(Config);

        Write(dir.Path, Make("a"));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(store.RefreshIfDue(Config));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(store.RefreshIfDue(Config));
    }

    [Fact]
    public void WatcherTriggersRefreshBeforePollInterval()
    {
        using var dir = new TempDir();
        var time = new ManualTime(2000);
        using var store = new SessionStore(dir.Path, time);
        store.StartWatching();
        store.RefreshIfDue(Config);

        Write(dir.Path, Make("a"));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!store.RefreshIfDue(Config) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.Contains("a", store.Sessions.Keys);
    }
}
