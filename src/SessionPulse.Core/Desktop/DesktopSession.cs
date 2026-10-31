namespace SessionPulse.Core.Desktop;

public enum DesktopSessionKind
{
    Cowork,
    Code,
}

/// <summary>
/// What we take from one Claude Desktop session metadata file. Everything else in that file
/// (system prompt, e-mail address, first message) is deliberately never read (docs/privacy.md).
/// </summary>
/// <param name="LocalId">The file name without extension, e.g. "local_1234". Used in deep links.</param>
/// <param name="CliSessionId">Equals the hook's session_id.</param>
public sealed record DesktopSession(
    DesktopSessionKind Kind,
    string LocalId,
    string? CliSessionId,
    string? Title,
    bool IsArchived,
    DateTimeOffset? CreatedAt,
    string? ScheduledTaskId)
{
    public string DeepLink => DeepLinks.ForSession(this.Kind, this.LocalId);
}

public static class DeepLinks
{
    /// <summary>Opens Claude Desktop without a specific session.</summary>
    public const string App = "claude://";

    /// <summary>A plain chat (used by the dictation feature).</summary>
    public static string ForChat(string chatId) => "claude://claude.ai/chat/" + Uri.EscapeDataString(chatId);

    public static string ForSession(DesktopSessionKind kind, string localId)
    {
        var id = Uri.EscapeDataString(localId);
        return kind == DesktopSessionKind.Cowork
            ? "claude://claude.ai/cowork/" + id
            : "claude://code/continue?session=" + id;
    }
}
