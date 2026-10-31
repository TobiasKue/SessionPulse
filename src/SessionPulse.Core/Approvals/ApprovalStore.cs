using System.Text.Json;
using System.Text.RegularExpressions;
using SessionPulse.Core.Sessions;

namespace SessionPulse.Core.Approvals;

public enum ApprovalRisk
{
    Normal,
    Edit,
    High,
}

public enum ApprovalDecision
{
    /// <summary>Allow this one call.</summary>
    Allow,

    /// <summary>Allow and add Claude's suggested rule for the rest of the session.</summary>
    AllowSession,

    /// <summary>Allow and save Claude's suggested rule (usually .claude/settings.local.json).</summary>
    AllowAlways,

    Deny,

    /// <summary>Hand the request back: Claude shows its normal permission dialog.</summary>
    Ask,
}

/// <summary>A permission request the hook is holding for a keypad decision (APPROVALS=1).</summary>
public sealed record PendingApproval(
    string Id,
    string SessionId,
    string Tool,
    string Preview,
    ApprovalRisk Risk,
    bool CanRemember,
    long Created,
    long Expires);

/// <summary>
/// The plugin's side of the file handshake with the hook (docs/status-format.md, "Approvals"):
/// requests are &lt;status dir&gt;/approvals/&lt;id&gt;.json, the answer is &lt;id&gt;.decision with one word.
/// Only ids of requests that exist are answered, so a decision cannot be planted for a future request.
/// </summary>
public sealed partial class ApprovalStore
{
    public const string FolderName = "approvals";

    private readonly string directory;
    private readonly TimeProvider time;
    private readonly Action<string> log;

    public ApprovalStore(string statusDirectory, TimeProvider time, Action<string>? log = null)
    {
        this.directory = Path.Combine(statusDirectory, FolderName);
        this.time = time;
        this.log = log ?? (_ => { });
    }

    /// <summary>Open requests, oldest first. Expired and unreadable requests are skipped.</summary>
    public IReadOnlyList<PendingApproval> Pending()
    {
        if (!Directory.Exists(this.directory))
        {
            return [];
        }

        var now = this.time.GetUtcNow().ToUnixTimeSeconds();
        var result = new List<PendingApproval>();
        string[] files;
        try
        {
            files = Directory.GetFiles(this.directory, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var file in files)
        {
            if (Read(file) is { } request && request.Expires > now && request.Id == Path.GetFileNameWithoutExtension(file))
            {
                result.Add(request);
            }
        }

        return result.OrderBy(r => r.Created).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Answers an open request. Returns false if it no longer exists (decided elsewhere, timed out).</summary>
    public bool Decide(string requestId, ApprovalDecision decision)
    {
        if (!IdPattern().IsMatch(requestId) || !File.Exists(Path.Combine(this.directory, requestId + ".json")))
        {
            return false;
        }

        var word = decision switch
        {
            ApprovalDecision.Allow => "allow",
            ApprovalDecision.AllowSession => "session",
            ApprovalDecision.AllowAlways => "always",
            ApprovalDecision.Deny => "deny",
            _ => "ask",
        };
        try
        {
            AtomicFile.WriteAllText(Path.Combine(this.directory, requestId + ".decision"), word + "\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.log("cannot answer approval request: " + ex.Message);
            return false;
        }
    }

    internal static PendingApproval? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 16 * 1024)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 1)
            {
                return null;
            }

            var id = Str(root, "id");
            var session = Str(root, "session");
            if (id is null || !IdPattern().IsMatch(id) || string.IsNullOrEmpty(session))
            {
                return null;
            }

            var risk = Str(root, "risk") switch
            {
                "high" => ApprovalRisk.High,
                "edit" => ApprovalRisk.Edit,
                _ => ApprovalRisk.Normal,
            };
            return new PendingApproval(
                id,
                session,
                Str(root, "tool") ?? "Tool",
                Str(root, "preview") ?? string.Empty,
                risk,
                root.TryGetProperty("remember", out var r) && r.ValueKind == JsonValueKind.True,
                Num(root, "created"),
                Num(root, "expires"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long Num(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : 0;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,200}$")]
    private static partial Regex IdPattern();
}
