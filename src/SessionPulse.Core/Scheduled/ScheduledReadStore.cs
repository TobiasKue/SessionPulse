using System.Text.Json;
using System.Text.Json.Serialization;
using SessionPulse.Core.Sessions;

namespace SessionPulse.Core.Scheduled;

/// <summary>
/// Read state of scheduled runs, which Claude Desktop does not keep (docs/LESSONS.md).
/// Persisted as scheduled-state.json: the run keys marked read, plus the time of the first start;
/// runs created before the start of that day count as read, so a fresh install does not flag the past.
/// </summary>
public sealed class ScheduledReadStore
{
    private readonly string filePath;
    private readonly Action<string> log;
    private readonly object gate = new();
    private State state;

    public ScheduledReadStore(string filePath, TimeProvider time, Action<string>? log = null)
    {
        this.filePath = filePath;
        this.log = log ?? (_ => { });
        this.state = this.Load() ?? this.CreateInitial(time);
    }

    /// <summary>Runs created before this moment count as read.</summary>
    public DateTimeOffset ReadBefore => DateTimeOffset.FromUnixTimeSeconds(this.state.ReadBefore);

    public bool IsRead(string runKey, DateTimeOffset? createdAt)
    {
        lock (this.gate)
        {
            return this.state.Read.Contains(runKey) || (createdAt is { } c && c.ToUnixTimeSeconds() < this.state.ReadBefore);
        }
    }

    public void MarkRead(string runKey)
    {
        lock (this.gate)
        {
            if (this.state.Read.Add(runKey))
            {
                this.Save();
            }
        }
    }

    /// <summary>Forgets read marks of runs that no longer exist.</summary>
    public void Prune(IEnumerable<string> liveRunKeys)
    {
        var live = liveRunKeys.ToHashSet(StringComparer.Ordinal);
        lock (this.gate)
        {
            if (this.state.Read.RemoveWhere(k => !live.Contains(k)) > 0)
            {
                this.Save();
            }
        }
    }

    private State CreateInitial(TimeProvider time)
    {
        var now = time.GetLocalNow();
        var startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var initial = new State { ReadBefore = startOfDay.ToUnixTimeSeconds() };
        lock (this.gate)
        {
            this.state = initial;
            this.Save();
        }

        return initial;
    }

    private State? Load()
    {
        try
        {
            if (File.Exists(this.filePath))
            {
                var loaded = JsonSerializer.Deserialize<State>(File.ReadAllText(this.filePath));
                if (loaded is { ReadBefore: > 0 })
                {
                    loaded.Read ??= new HashSet<string>(StringComparer.Ordinal);
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this.log("cannot read " + this.filePath + ": " + ex.Message);
        }

        return null;
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(this.filePath, JsonSerializer.Serialize(this.state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.log("cannot write " + this.filePath + ": " + ex.Message);
        }
    }

    private sealed class State
    {
        [JsonPropertyName("readBefore")]
        public long ReadBefore { get; set; }

        [JsonPropertyName("read")]
        public HashSet<string> Read { get; set; } = new(StringComparer.Ordinal);
    }
}
