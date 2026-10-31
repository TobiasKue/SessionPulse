namespace SessionPulse.Core.Sessions;

/// <summary>A session that wants a key, with what decides who gets one.</summary>
/// <param name="Rank">0 = waiting, 1 = working, 2 = done, 3 = idle (lower is more important).</param>
/// <param name="Recency">Unix seconds of the last activity; newer wins among equal ranks.</param>
public readonly record struct SlotCandidate(string Id, int Rank, long Recency);

/// <summary>
/// Assigns sessions to a fixed number of keys. A session keeps its key for as long as it is visible,
/// so keys don't jump around. Free keys are filled by rank and recency. When all keys are taken, a
/// waiting or working session may take over the key of the least important done or idle session.
/// </summary>
public sealed class SlotAssigner
{
    private const int MaxDisplaceableRank = 1;

    private readonly string?[] slots;

    public SlotAssigner(int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);
        this.slots = new string?[slotCount];
    }

    public int Count => this.slots.Length;

    /// <summary>Session id per key (index 0 = key 1), null for an empty key.</summary>
    public IReadOnlyList<string?> Slots => this.slots;

    /// <summary>Updates the assignment for the currently visible sessions. Returns true if any key changed.</summary>
    public bool Update(IReadOnlyCollection<SlotCandidate> visible)
    {
        var before = (string?[])this.slots.Clone();
        var byId = visible.ToDictionary(c => c.Id, StringComparer.Ordinal);

        for (var i = 0; i < this.slots.Length; i++)
        {
            if (this.slots[i] is { } id && !byId.ContainsKey(id))
            {
                this.slots[i] = null;
            }
        }

        var assigned = this.slots.Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        var waiting = visible
            .Where(c => !assigned.Contains(c.Id))
            .OrderBy(c => c.Rank)
            .ThenByDescending(c => c.Recency)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in waiting)
        {
            var free = Array.IndexOf(this.slots, null);
            if (free >= 0)
            {
                this.slots[free] = candidate.Id;
                continue;
            }

            if (candidate.Rank > MaxDisplaceableRank)
            {
                continue;
            }

            // Take over the key of the least important session that is less important than this one.
            var victim = -1;
            for (var i = 0; i < this.slots.Length; i++)
            {
                var current = byId[this.slots[i]!];
                if (current.Rank <= candidate.Rank)
                {
                    continue;
                }

                if (victim < 0 || IsLessImportant(current, byId[this.slots[victim]!]))
                {
                    victim = i;
                }
            }

            if (victim >= 0)
            {
                this.slots[victim] = candidate.Id;
            }
        }

        return !before.SequenceEqual(this.slots);
    }

    private static bool IsLessImportant(SlotCandidate a, SlotCandidate b) =>
        a.Rank != b.Rank ? a.Rank > b.Rank : a.Recency < b.Recency;
}
