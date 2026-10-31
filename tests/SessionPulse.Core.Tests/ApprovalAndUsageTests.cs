using SessionPulse.Core.Approvals;
using SessionPulse.Core.Usage;

namespace SessionPulse.Core.Tests;

public sealed class ApprovalAndUsageTests : IDisposable
{
    private const long Now = 1_800_000_000;

    private readonly TempDir dir = new();
    private readonly ManualTime time = new(Now);

    public void Dispose() => this.dir.Dispose();

    private string WriteRequest(string id, long expires = Now + 30, string risk = "high", string? fileName = null, int version = 1)
    {
        var folder = this.dir.Combine("approvals");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, (fileName ?? id) + ".json");
        File.WriteAllText(path, $$"""{"v":{{version}},"id":"{{id}}","session":"s1","tool":"Bash","preview":"rm -rf build","risk":"{{risk}}","remember":true,"created":{{Now - 1}},"expires":{{expires}}}""");
        return path;
    }

    [Fact]
    public void ListsOpenRequestsOldestFirst()
    {
        this.WriteRequest("b-2");
        this.WriteRequest("a-1", risk: "edit");
        this.WriteRequest("expired", expires: Now - 1);
        this.WriteRequest("other", fileName: "renamed");
        this.WriteRequest("old-format", version: 2);
        File.WriteAllText(this.dir.Combine("approvals", "broken.json"), "{");

        var pending = new ApprovalStore(this.dir.Path, this.time).Pending();

        Assert.Equal(["a-1", "b-2"], pending.Select(p => p.Id));
        var first = pending[0];
        Assert.Equal(("s1", "Bash", "rm -rf build", ApprovalRisk.Edit, true), (first.SessionId, first.Tool, first.Preview, first.Risk, first.CanRemember));
        Assert.Equal(ApprovalRisk.High, pending[1].Risk);
    }

    [Theory]
    [InlineData(ApprovalDecision.Allow, "allow")]
    [InlineData(ApprovalDecision.AllowSession, "session")]
    [InlineData(ApprovalDecision.AllowAlways, "always")]
    [InlineData(ApprovalDecision.Deny, "deny")]
    [InlineData(ApprovalDecision.Ask, "ask")]
    public void DecisionIsWrittenNextToTheRequest(ApprovalDecision decision, string word)
    {
        this.WriteRequest("req-1");
        var store = new ApprovalStore(this.dir.Path, this.time);

        Assert.True(store.Decide("req-1", decision));

        Assert.Equal(word, File.ReadAllText(this.dir.Combine("approvals", "req-1.decision")).Trim());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("../evil")]
    [InlineData("")]
    public void OnlyExistingRequestsCanBeAnswered(string id)
    {
        this.WriteRequest("req-1");
        var store = new ApprovalStore(this.dir.Path, this.time);

        Assert.False(store.Decide(id, ApprovalDecision.Allow));
        Assert.False(File.Exists(this.dir.Combine("approvals", id + ".decision")));
    }

    [Fact]
    public void NoFolderMeansNothingPending() => Assert.Empty(new ApprovalStore(this.dir.Combine("none"), this.time).Pending());

    [Fact]
    public void ParsesUsageAndResetsExpiredWindows()
    {
        var usage = UsageStore.Parse("""{"v":1,"updated":5,"fiveHour":{"pct":93.6,"resetsAt":1000},"sevenDay":{"pct":41,"resetsAt":5000}}""")!;

        Assert.Equal(5, usage.Updated);
        Assert.Equal(new UsageWindow(93.6, 1000), usage.FiveHour);
        Assert.Null(usage.Spend);
        Assert.Equal(93.6, usage.MaxPercent(999));
        Assert.Equal(41, usage.MaxPercent(1000));
        Assert.Equal("1:05h", UsageStore.TimeLeft(1000 + 3900, 1000));
        Assert.Equal(string.Empty, UsageStore.TimeLeft(1000, 2000));
    }

    [Theory]
    [InlineData("""{"v":2}""")]
    [InlineData("nope")]
    [InlineData("[]")]
    public void RejectsUnknownUsageFiles(string json) => Assert.Null(UsageStore.Parse(json));

    [Fact]
    public void UsageStoreFollowsTheFile()
    {
        var store = new UsageStore(this.dir.Path);
        Assert.Null(store.Current);

        File.WriteAllText(this.dir.Combine("usage.json"), """{"v":1,"updated":1,"fiveHour":{"pct":10,"resetsAt":0}}""");
        Assert.Equal(10, store.Current!.FiveHour!.Value.Percent);

        File.WriteAllText(this.dir.Combine("usage.json"), """{"v":1,"updated":2,"fiveHour":{"pct":20,"resetsAt":0}}""");
        File.SetLastWriteTimeUtc(this.dir.Combine("usage.json"), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(20, store.Current!.FiveHour!.Value.Percent);
    }
}
