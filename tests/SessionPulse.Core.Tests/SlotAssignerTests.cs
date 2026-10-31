using SessionPulse.Core.Sessions;

namespace SessionPulse.Core.Tests;

public class SlotAssignerTests
{
    private static SlotCandidate C(string id, int rank, long recency = 0) => new(id, rank, recency);

    [Fact]
    public void FillsFreeKeysByRankThenRecency()
    {
        var a = new SlotAssigner(4);

        a.Update([C("done", 2, 50), C("work-old", 1, 10), C("wait", 0, 1), C("work-new", 1, 20)]);

        Assert.Equal(["wait", "work-new", "work-old", "done"], a.Slots);
    }

    [Fact]
    public void SessionsKeepTheirKeys()
    {
        var a = new SlotAssigner(3);
        a.Update([C("a", 2), C("b", 2), C("c", 2)]);

        // b leaves, a turns into "waiting", d arrives: a stays on key 1, d takes b's key.
        var changed = a.Update([C("a", 0), C("c", 2), C("d", 1)]);

        Assert.True(changed);
        Assert.Equal(["a", "d", "c"], a.Slots);
        Assert.False(a.Update([C("a", 0), C("c", 2), C("d", 1)]));
    }

    [Fact]
    public void ActiveSessionTakesOverLeastImportantKeyWhenFull()
    {
        var a = new SlotAssigner(3);
        a.Update([C("done-new", 2, 30), C("idle", 3, 99), C("done-old", 2, 10)]);
        Assert.Equal(["done-new", "done-old", "idle"], a.Slots);

        // The idle session is the least important one.
        a.Update([C("done-new", 2, 30), C("idle", 3, 99), C("done-old", 2, 10), C("wait", 0, 1)]);
        Assert.Equal(["done-new", "done-old", "wait"], a.Slots);

        // Among finished sessions, the oldest goes.
        a.Update([C("done-new", 2, 30), C("wait", 0, 1), C("done-old", 2, 10), C("work", 1, 5)]);
        Assert.Equal(["done-new", "work", "wait"], a.Slots);
    }

    [Fact]
    public void FinishedOrIdleSessionsNeverDisplaceOthers()
    {
        var a = new SlotAssigner(2);
        a.Update([C("w1", 1), C("w2", 1)]);

        a.Update([C("w1", 1), C("w2", 1), C("done", 2, 999), C("idle", 3, 999)]);

        Assert.Equal(["w1", "w2"], a.Slots);
    }

    [Fact]
    public void WorkingDoesNotDisplaceWaitingOrWorking()
    {
        var a = new SlotAssigner(2);
        a.Update([C("wait", 0), C("work", 1, 1)]);

        a.Update([C("wait", 0), C("work", 1, 1), C("work-new", 1, 99)]);

        Assert.Equal(["wait", "work"], a.Slots);
    }

    [Fact]
    public void EmptyInputClearsAllKeys()
    {
        var a = new SlotAssigner(2);
        a.Update([C("a", 1)]);

        Assert.True(a.Update([]));
        Assert.Equal([null, null], a.Slots);
    }
}
