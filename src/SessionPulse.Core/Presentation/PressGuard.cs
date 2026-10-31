namespace SessionPulse.Core.Presentation;

/// <summary>
/// Protection against "ghost presses" (docs/LESSONS.md): the service runs commands on key *down*. If a
/// folder closes right away, the key *release* lands on the main page and triggers whatever key sits at
/// the same position. So folders close only <see cref="CloseDelay"/> after a press, and presses are
/// ignored until <see cref="IgnoreAfterClose"/> after a programmatic close.
/// </summary>
public sealed class PressGuard
{
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan IgnoreAfterClose = TimeSpan.FromMilliseconds(700);

    private readonly TimeProvider time;
    private long ignoreUntilTicks;

    public PressGuard(TimeProvider time) => this.time = time;

    /// <summary>Call right after closing a folder from code.</summary>
    public void FolderClosed() =>
        Interlocked.Exchange(ref this.ignoreUntilTicks, (this.time.GetUtcNow() + IgnoreAfterClose).UtcTicks);

    /// <summary>True if a press now is probably the release of the press that closed a folder.</summary>
    public bool ShouldIgnore() => this.time.GetUtcNow().UtcTicks < Interlocked.Read(ref this.ignoreUntilTicks);
}
