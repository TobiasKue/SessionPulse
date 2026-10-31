using System.Text;
using System.Text.Json;

namespace SessionPulse.Core.Status;

public enum StatusParseOutcome
{
    Ok,
    UnsupportedVersion,
    Invalid,
}

public readonly record struct StatusParseResult(StatusParseOutcome Outcome, SessionStatus? Status, string? Error)
{
    public static StatusParseResult Ok(SessionStatus status) => new(StatusParseOutcome.Ok, status, null);

    public static StatusParseResult Unsupported(string error) => new(StatusParseOutcome.UnsupportedVersion, null, error);

    public static StatusParseResult Invalid(string error) => new(StatusParseOutcome.Invalid, null, error);
}

/// <summary>
/// Parses status files written by the hook. Lenient where the hook may produce imperfect data
/// (truncated multi-byte characters, unknown fields, unknown detail codes), strict where a wrong
/// guess would mislead the user (version, id, state).
/// </summary>
public static class StatusFileParser
{
    public const int SupportedVersion = 2;

    /// <summary>Status lines are well below 1 KB; anything larger is not ours.</summary>
    public const int MaxFileBytes = 64 * 1024;

    // Invalid UTF-8 (e.g. a label cut inside a character) becomes U+FFFD instead of failing.
    private static readonly Encoding LenientUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static StatusParseResult Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return StatusParseResult.Invalid("empty");
        }

        if (bytes.Length > MaxFileBytes)
        {
            return StatusParseResult.Invalid("too large");
        }

        var text = LenientUtf8.GetString(bytes);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Parse(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return StatusParseResult.Invalid("not JSON: " + ex.Message);
        }
    }

    private static StatusParseResult Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return StatusParseResult.Invalid("not an object");
        }

        if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version))
        {
            return StatusParseResult.Invalid("missing version");
        }

        if (version != SupportedVersion)
        {
            return StatusParseResult.Unsupported($"version {version}, expected {SupportedVersion}");
        }

        var id = GetString(root, "id");
        if (string.IsNullOrEmpty(id))
        {
            return StatusParseResult.Invalid("missing id");
        }

        if (!TryParseState(GetString(root, "state"), out var state))
        {
            return StatusParseResult.Invalid("unknown state");
        }

        var status = new SessionStatus(
            Id: id,
            Source: GetString(root, "src") == "cowork" ? SessionSource.Cowork : SessionSource.Code,
            Label: GetString(root, "label") ?? string.Empty,
            ScheduledTask: NullIfEmpty(GetString(root, "sched")),
            State: state,
            Detail: ParseDetail(GetString(root, "detail")),
            Tool: NullIfEmpty(GetString(root, "tool")),
            Event: GetString(root, "event") ?? string.Empty,
            Started: GetInt64(root, "started"),
            TurnStarted: GetInt64(root, "turnStarted"),
            Activity: GetInt64(root, "activity"),
            Updated: GetInt64(root, "updated"),
            Prompts: GetInt32(root, "prompts"),
            Tools: GetInt32(root, "tools"),
            TasksDone: GetInt32(root, "tasksDone"),
            TasksTotal: GetInt32(root, "tasksTotal"),
            Title: NullIfEmpty(GetString(root, "title")),
            Host: NullIfEmpty(GetString(root, "host")),
            ProcessId: GetInt32(root, "pid"),
            Wsl: NullIfEmpty(GetString(root, "wsl")));
        return StatusParseResult.Ok(status);
    }

    internal static bool TryParseState(string? value, out SessionState state)
    {
        switch (value)
        {
            case "idle": state = SessionState.Idle; return true;
            case "working": state = SessionState.Working; return true;
            case "waiting": state = SessionState.Waiting; return true;
            case "done": state = SessionState.Done; return true;
            case "ended": state = SessionState.Ended; return true;
            default: state = default; return false;
        }
    }

    internal static StatusDetail ParseDetail(string? value) => value switch
    {
        "idle" => StatusDetail.Idle,
        "thinking" => StatusDetail.Thinking,
        "tool" => StatusDetail.Tool,
        "question" => StatusDetail.Question,
        "plan" => StatusDetail.Plan,
        "permission" => StatusDetail.Permission,
        "input" => StatusDetail.Input,
        "subagent" => StatusDetail.Subagent,
        "interrupted" => StatusDetail.Interrupted,
        "done" => StatusDetail.Done,
        "error" => StatusDetail.Error,
        "ended" => StatusDetail.Ended,
        "compacting" => StatusDetail.Compacting,
        "limit" => StatusDetail.Limit,
        _ => StatusDetail.Unknown,
    };

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static long GetInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) && n > 0 ? n : 0;

    private static int GetInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) && n > 0 ? n : 0;
}
