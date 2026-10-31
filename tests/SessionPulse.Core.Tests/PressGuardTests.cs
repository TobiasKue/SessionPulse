using SessionPulse.Core.Presentation;

namespace SessionPulse.Core.Tests;

public class PressGuardTests
{
    [Fact]
    public void IgnoresPressesShortlyAfterAProgrammaticClose()
    {
        var time = new ManualTime(1000);
        var guard = new PressGuard(time);
        Assert.False(guard.ShouldIgnore());

        guard.FolderClosed();
        Assert.True(guard.ShouldIgnore());

        time.Advance(TimeSpan.FromMilliseconds(699));
        Assert.True(guard.ShouldIgnore());

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(guard.ShouldIgnore());
    }

    [Fact]
    public void CloseDelayIsShorterThanIgnoreWindow() =>
        Assert.True(PressGuard.CloseDelay < PressGuard.IgnoreAfterClose);
}
