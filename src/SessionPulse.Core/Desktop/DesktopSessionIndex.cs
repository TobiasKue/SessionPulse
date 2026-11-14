using SessionPulse.Core.Config;

namespace SessionPulse.Core.Desktop;

/// <summary>
/// Index of Claude Desktop sessions by cliSessionId (= hook session_id). Layout on Windows:
/// %LOCALAPPDATA%\Claude-Data\claude_*\{local-agent-mode-sessions|claude-code-sessions}\&lt;account&gt;\&lt;org&gt;\local_*.json,
/// on macOS without the claude_* level: ~/Library/Application Support/Claude/{...}/&lt;account&gt;/&lt;org&gt;/local_*.json
/// Rescans at most every <see cref="MinRescanInterval"/> and only re-reads files whose time stamp changed.
/// </summary>
public sealed class DesktopSessionIndex
{
    public static readonly TimeSpan MinRescanInterval = TimeSpan.FromSeconds(15);

    private static readonly (string Folder, DesktopSessionKind Kind)[] KindFolders =
    [
        ("local-agent-mode-sessions", DesktopSessionKind.Cowork),
        ("claude-code-sessions", DesktopSessionKind.Code),
    ];

    private readonly string? dataRoot;
    private readonly TimeProvider time;
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTime Stamp, DesktopSession? Session)> files = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DesktopSession> byCliId = new(StringComparer.Ordinal);
    private DateTimeOffset lastScan = DateTimeOffset.MinValue;

    /// <param name="dataRoot">The Claude-Data directory, or null if unknown on this platform.</param>
    public DesktopSessionIndex(string? dataRoot, TimeProvider time)
    {
        this.dataRoot = dataRoot;
        this.time = time;
    }

    public static string? DefaultDataRoot(Func<string, string?> getEnvironmentVariable) =>
        DefaultDataRoot(StatusPaths.CurrentPlatform, getEnvironmentVariable);

    public static string? DefaultDataRoot(HostPlatform platform, Func<string, string?> getEnvironmentVariable)
    {
        if (platform == HostPlatform.MacOS)
        {
            var home = getEnvironmentVariable("HOME");
            return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Library", "Application Support", "Claude");
        }

        var local = getEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Claude-Data");
    }

    public IReadOnlyDictionary<string, DesktopSession> Sessions
    {
        get
        {
            lock (this.gate)
            {
                return this.byCliId;
            }
        }
    }

    public DesktopSession? Find(string cliSessionId)
    {
        lock (this.gate)
        {
            return this.byCliId.GetValueOrDefault(cliSessionId);
        }
    }

    /// <summary>Rescans if the minimum interval has passed (or <paramref name="force"/>). Returns true if the index changed.</summary>
    public bool RescanIfDue(bool force = false)
    {
        var now = this.time.GetUtcNow();
        if (!force && now - this.lastScan < MinRescanInterval)
        {
            return false;
        }

        this.lastScan = now;
        if (this.dataRoot is null || !Directory.Exists(this.dataRoot))
        {
            return this.Publish([]);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, kind) in this.EnumerateMetadataFiles())
        {
            seen.Add(path);
            DateTime stamp;
            try
            {
                stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!this.files.TryGetValue(path, out var cached) || cached.Stamp != stamp)
            {
                this.files[path] = (stamp, DesktopMetadataReader.Read(path, kind));
            }
        }

        foreach (var gone in this.files.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            this.files.Remove(gone);
        }

        return this.Publish(this.files.Values.Select(v => v.Session).OfType<DesktopSession>());
    }

    private IEnumerable<(string Path, DesktopSessionKind Kind)> EnumerateMetadataFiles()
    {
        // Windows: one claude_* folder per installation. macOS: the kind folders are right in the data root.
        foreach (var install in SafeDirectories(this.dataRoot!, "claude_*").Prepend(this.dataRoot!))
        {
            foreach (var (folder, kind) in KindFolders)
            {
                var root = Path.Combine(install, folder);
                foreach (var account in SafeDirectories(root, "*"))
                {
                    foreach (var org in SafeDirectories(account, "*"))
                    {
                        foreach (var file in SafeFiles(org, "local_*.json"))
                        {
                            yield return (file, kind);
                        }
                    }
                }
            }
        }
    }

    private bool Publish(IEnumerable<DesktopSession> sessions)
    {
        var next = new Dictionary<string, DesktopSession>(StringComparer.Ordinal);
        foreach (var s in sessions)
        {
            if (s.CliSessionId is { Length: > 0 } id)
            {
                // Should be unique; if not, prefer the newer entry.
                if (!next.TryGetValue(id, out var existing) || (s.CreatedAt ?? default) > (existing.CreatedAt ?? default))
                {
                    next[id] = s;
                }
            }
        }

        lock (this.gate)
        {
            var changed = next.Count != this.byCliId.Count
                || next.Any(kv => !this.byCliId.TryGetValue(kv.Key, out var old) || old != kv.Value);
            this.byCliId = next;
            return changed;
        }
    }

    private static string[] SafeDirectories(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] SafeFiles(string path, string pattern)
    {
        try
        {
            return Directory.GetFiles(path, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
