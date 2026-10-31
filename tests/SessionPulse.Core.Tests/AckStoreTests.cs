using SessionPulse.Core.Sessions;
using SessionPulse.Core.Status;
using static SessionPulse.Core.Tests.StatusFiles;

namespace SessionPulse.Core.Tests;

public class AckStoreTests
{
    [Fact]
    public void AcknowledgementHoldsUntilACounterGrows()
    {
        using var dir = new TempDir();
        var acks = new AckStore(dir.Combine("acked.json"));
        var done = Make("s", SessionState.Done, prompts: 2, tools: 5);

        Assert.False(acks.IsAcked(done));
        Assert.True(acks.Ack(done));

        Assert.True(acks.IsAcked(done));
        Assert.True(acks.IsAcked(done with { State = SessionState.Ended, Updated = 99999 }));
        Assert.False(acks.IsAcked(done with { Tools = 6 }));
        Assert.False(acks.IsAcked(done with { Prompts = 3 }));
        Assert.False(acks.IsAcked(done with { State = SessionState.Working }));
    }

    [Fact]
    public void RunningSessionsCannotBeAcknowledged()
    {
        using var dir = new TempDir();
        var acks = new AckStore(dir.Combine("acked.json"));

        Assert.False(acks.Ack(Make("s", SessionState.Waiting)));
        Assert.False(File.Exists(dir.Combine("acked.json")));
    }

    [Fact]
    public void PersistsAndPrunes()
    {
        using var dir = new TempDir();
        var path = dir.Combine("sub", "acked.json");
        var a = Make("a", SessionState.Done);
        var b = Make("b", SessionState.Done);
        var first = new AckStore(path);
        first.Ack(a);
        first.Ack(b);

        first.Prune(["a"]);
        var second = new AckStore(path);

        Assert.True(second.IsAcked(a));
        Assert.False(second.IsAcked(b));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"a":[1]}""")]
    [InlineData("[]")]
    public void BrokenFileStartsEmpty(string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("acked.json"), content);
        var logged = new List<string>();

        var acks = new AckStore(dir.Combine("acked.json"), logged.Add);

        Assert.False(acks.IsAcked(Make("a", SessionState.Done, prompts: 1)));
    }
}
