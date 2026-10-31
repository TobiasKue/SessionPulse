namespace SessionPulse.Core.Status;

public enum SessionState
{
    Idle,
    Working,
    Waiting,
    Done,
    Ended,
}

public enum SessionSource
{
    Code,
    Cowork,
}

/// <summary>Machine-readable detail code written by the hook; translated for display by the plugin.</summary>
public enum StatusDetail
{
    /// <summary>A code this version does not know yet (codes may be added without a format version bump).</summary>
    Unknown,
    Idle,
    Thinking,
    Tool,
    Question,
    Plan,
    Permission,
    Input,
    Subagent,
    Interrupted,
    Done,
    Error,
    Ended,

    /// <summary>The conversation is being compacted (PreCompact).</summary>
    Compacting,

    /// <summary>The turn ended because a usage limit was reached (StopFailure rate_limit).</summary>
    Limit,
}

/// <summary>
/// One parsed status file (format v2, see docs/status-format.md). Times are Unix seconds, 0 = never.
/// <see cref="Title"/>, <see cref="Host"/>, <see cref="ProcessId"/> and <see cref="Wsl"/> are optional fields
/// that older hooks don't write.
/// </summary>
public sealed record SessionStatus(
    string Id,
    SessionSource Source,
    string Label,
    string? ScheduledTask,
    SessionState State,
    StatusDetail Detail,
    string? Tool,
    string Event,
    long Started,
    long TurnStarted,
    long Activity,
    long Updated,
    int Prompts,
    int Tools,
    int TasksDone,
    int TasksTotal,
    string? Title = null,
    string? Host = null,
    int ProcessId = 0,
    string? Wsl = null)
{
    /// <summary>Value of <see cref="Host"/> (CLAUDE_CODE_ENTRYPOINT) for sessions of the Claude Desktop app.</summary>
    public const string DesktopHost = "claude-desktop";

    /// <summary>Cowork sends SessionEnd after every answer, so "ended" counts as finished, not as gone.</summary>
    public bool IsFinished => this.State is SessionState.Done or SessionState.Ended;

    /// <summary>
    /// The hook reported a Claude Code that is not the desktop app (e.g. the CLI in a terminal). Unknown for
    /// status files of older hooks, which are treated as desktop sessions.
    /// </summary>
    public bool RunsOutsideDesktop => !string.IsNullOrEmpty(this.Host) && this.Host != DesktopHost;

    public bool NeedsUser => this.State == SessionState.Waiting;
}
