using System.Text.Json;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Sessions;

/// <summary>
/// Remembers which finished sessions the user has acknowledged ("seen"). An acknowledgement stores
/// the counters [prompts, tools] at that moment and holds until one of them grows. Timestamps are no
/// use here: Cowork touches sessions just by opening them (docs/LESSONS.md).
/// Persisted as acked.json: {"&lt;session id&gt;": [prompts, tools], ...}.
/// </summary>
public sealed class AckStore
{
    private readonly string filePath;
    private readonly Action<string> log;
    private readonly object gate = new();
    private Dictionary<string, int[]> acks;

    public AckStore(string filePath, Action<string>? log = null)
    {
        this.filePath = filePath;
        this.log = log ?? (_ => { });
        this.acks = this.Load();
    }

    public bool IsAcked(SessionStatus status)
    {
        if (!status.IsFinished)
        {
            return false;
        }

        lock (this.gate)
        {
            return this.acks.TryGetValue(status.Id, out var counters)
                && status.Prompts <= counters[0]
                && status.Tools <= counters[1];
        }
    }

    /// <summary>Acknowledges a finished session. Returns false (and does nothing) for running sessions.</summary>
    public bool Ack(SessionStatus status)
    {
        if (!status.IsFinished)
        {
            return false;
        }

        lock (this.gate)
        {
            this.acks[status.Id] = [status.Prompts, status.Tools];
            this.Save();
        }

        return true;
    }

    /// <summary>Forgets acknowledgements of sessions that no longer exist.</summary>
    public void Prune(IEnumerable<string> liveSessionIds)
    {
        var live = liveSessionIds.ToHashSet(StringComparer.Ordinal);
        lock (this.gate)
        {
            var removed = this.acks.Keys.Where(id => !live.Contains(id)).ToList();
            if (removed.Count == 0)
            {
                return;
            }

            foreach (var id in removed)
            {
                this.acks.Remove(id);
            }

            this.Save();
        }
    }

    private Dictionary<string, int[]> Load()
    {
        try
        {
            if (File.Exists(this.filePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(this.filePath));
                if (loaded is not null)
                {
                    return loaded
                        .Where(kv => kv.Value is { Length: 2 })
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this.log("cannot read " + this.filePath + ", starting without acknowledgements: " + ex.Message);
        }

        return new Dictionary<string, int[]>(StringComparer.Ordinal);
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(this.filePath, JsonSerializer.Serialize(this.acks));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.log("cannot write " + this.filePath + ": " + ex.Message);
        }
    }
}
