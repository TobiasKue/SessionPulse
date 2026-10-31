using SessionPulse.Core.Config;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Sessions;

/// <summary>
/// In-memory view of &lt;status dir&gt;/sessions. A FileSystemWatcher only marks the store dirty; the
/// actual reading happens in <see cref="RefreshIfDue"/>, called from the plugin's one-second tick, and
/// at least every <see cref="PollInterval"/> because watcher events can get lost.
/// </summary>
public sealed class SessionStore : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Leftovers of interrupted hook writes older than this are deleted.</summary>
    private static readonly TimeSpan TempFileMaxAge = TimeSpan.FromHours(1);

    private readonly string sessionsDirectory;
    private readonly TimeProvider time;
    private readonly Action<string> log;
    private readonly object gate = new();
    private readonly HashSet<string> loggedProblems = new(StringComparer.Ordinal);
    private Dictionary<string, SessionStatus> sessions = new(StringComparer.Ordinal);
    private FileSystemWatcher? watcher;
    private volatile bool dirty = true;
    private DateTimeOffset lastRefresh = DateTimeOffset.MinValue;

    public SessionStore(string statusDirectory, TimeProvider time, Action<string>? log = null)
    {
        this.sessionsDirectory = Path.Combine(statusDirectory, StatusPaths.SessionsFolderName);
        this.time = time;
        this.log = log ?? (_ => { });
    }

    public string SessionsDirectory => this.sessionsDirectory;

    /// <summary>Snapshot of all known sessions, keyed by id.</summary>
    public IReadOnlyDictionary<string, SessionStatus> Sessions
    {
        get
        {
            lock (this.gate)
            {
                return this.sessions;
            }
        }
    }

    /// <summary>True once the sessions directory has existed at least once (the hook ran).</summary>
    public bool HookSeen { get; private set; }

    public void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(this.sessionsDirectory);
            this.watcher = new FileSystemWatcher(this.sessionsDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            this.watcher.Changed += (_, _) => this.dirty = true;
            this.watcher.Created += (_, _) => this.dirty = true;
            this.watcher.Deleted += (_, _) => this.dirty = true;
            this.watcher.Renamed += (_, _) => this.dirty = true;
            this.watcher.Error += (_, e) =>
            {
                this.dirty = true;
                this.log("file watcher error, relying on polling: " + e.GetException().Message);
            };
            this.watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            this.log("cannot watch " + this.sessionsDirectory + ", relying on polling: " + ex.Message);
        }
    }

    /// <summary>Refreshes when the watcher reported a change or the poll interval has passed. Returns true if sessions changed.</summary>
    public bool RefreshIfDue(SessionPulseConfig config)
    {
        var now = this.time.GetUtcNow();
        if (!this.dirty && now - this.lastRefresh < PollInterval)
        {
            return false;
        }

        return this.Refresh(config);
    }

    /// <summary>Re-reads all status files and deletes outdated ones. Returns true if sessions changed.</summary>
    public bool Refresh(SessionPulseConfig config)
    {
        this.dirty = false;
        var now = this.time.GetUtcNow();
        this.lastRefresh = now;

        if (!Directory.Exists(this.sessionsDirectory))
        {
            return this.Replace(new Dictionary<string, SessionStatus>(StringComparer.Ordinal));
        }

        this.HookSeen = true;
        Dictionary<string, SessionStatus> previous;
        lock (this.gate)
        {
            previous = this.sessions;
        }

        var next = new Dictionary<string, SessionStatus>(StringComparer.Ordinal);
        var cleanupBefore = now - TimeSpan.FromHours(config.CleanupHours);
        foreach (var path in SafeEnumerate(this.sessionsDirectory))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.') && name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                DeleteIfOlder(path, now - TempFileMaxAge);
                continue;
            }

            if (!name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            var id = name[..^".json".Length];
            var result = StatusFileReader.Read(path);
            if (result.Outcome == StatusParseOutcome.Ok)
            {
                var status = result.Status!;
                if (status.Updated > 0 && DateTimeOffset.FromUnixTimeSeconds(status.Updated) < cleanupBefore)
                {
                    this.TryDelete(path, "outdated");
                    continue;
                }

                next[status.Id] = status;
                this.loggedProblems.Remove(id);
            }
            else
            {
                // A file that is broken right now (or has another version) keeps its last good state.
                if (previous.TryGetValue(id, out var old))
                {
                    next[id] = old;
                }

                if (this.loggedProblems.Add(id))
                {
                    this.log($"ignoring status file {name}: {result.Error}");
                }

                if (result.Outcome == StatusParseOutcome.Invalid)
                {
                    DeleteIfOlder(path, cleanupBefore);
                }
            }
        }

        return this.Replace(next);
    }

    public void Dispose() => this.watcher?.Dispose();

    private bool Replace(Dictionary<string, SessionStatus> next)
    {
        lock (this.gate)
        {
            var changed = next.Count != this.sessions.Count
                || next.Any(kv => !this.sessions.TryGetValue(kv.Key, out var old) || old != kv.Value);
            this.sessions = next;
            return changed;
        }
    }

    private static IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void DeleteIfOlder(string path, DateTimeOffset before)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(path) < before.UtcDateTime)
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryDelete(string path, string reason)
    {
        try
        {
            File.Delete(path);
            this.log($"deleted {reason} status file {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
