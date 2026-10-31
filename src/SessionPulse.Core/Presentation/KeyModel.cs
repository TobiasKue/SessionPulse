using SessionPulse.Core.Status;

namespace SessionPulse.Core.Presentation;

public enum KeyState
{
    Empty,
    Idle,
    Working,
    Waiting,
    Done,
    Stuck,

    /// <summary>Working, but compacting the conversation.</summary>
    Compacting,

    /// <summary>Finished with an error or because a usage limit was reached.</summary>
    Failed,
}

/// <summary>
/// Everything a session key shows, independent of how it is drawn. Two equal models draw the same
/// image (apart from animation frames), so equality is the redraw signature. <see cref="Unread"/> marks
/// scheduled runs nobody has looked at yet.
/// </summary>
public sealed record KeyModel(
    int Number,
    KeyState State,
    string? SessionId,
    SessionSource Source,
    string Title,
    string Detail,
    string Elapsed,
    int TasksDone,
    int TasksTotal,
    bool Unread = false)
{
    public static KeyModel Empty(int number) =>
        new(number, KeyState.Empty, null, SessionSource.Code, string.Empty, string.Empty, string.Empty, 0, 0);

    /// <summary>Spinner (working) and pulse (waiting) need a redraw on every tick.</summary>
    public bool IsAnimated => this.State is KeyState.Working or KeyState.Waiting or KeyState.Compacting;

    /// <summary>Progress between 0 and 1, or null if the session has no task list.</summary>
    public double? Progress => this.TasksTotal > 0 ? Math.Clamp((double)this.TasksDone / this.TasksTotal, 0, 1) : null;
}
