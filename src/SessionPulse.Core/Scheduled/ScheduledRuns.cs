using SessionPulse.Core.Desktop;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Scheduled;

/// <summary>The newest run of one scheduled task, as shown in the "scheduled" folder.</summary>
/// <param name="RunKey">Stable key of the run: the Claude Desktop local id, or "hook_&lt;id&gt;" without metadata.</param>
public sealed record ScheduledRun(
    string TaskKey,
    string RunKey,
    string Title,
    DateTimeOffset? CreatedAt,
    DesktopSession? Desktop,
    SessionStatus? Status,
    bool Unread);

/// <summary>
/// Finds the newest non-archived run per scheduled task. Two sources, because neither is complete
/// (docs/LESSONS.md): Claude Desktop metadata (scheduledTaskId, sometimes beyond the 64 KB we read) and
/// the hook's status file (sched = task name; gone after the cleanup period). Titles that are still the
/// raw "&lt;scheduled-task ...&gt;" prompt are replaced by the title of an earlier run of the same task.
/// </summary>
public static class ScheduledRuns
{
    private const string RawPromptPrefix = "<scheduled-task";

    public static IReadOnlyList<ScheduledRun> Collect(
        IEnumerable<DesktopSession> desktopSessions,
        IReadOnlyDictionary<string, SessionStatus> statuses,
        ScheduledReadStore readStore,
        string fallbackTitle)
    {
        var desktop = desktopSessions.ToList();
        var byCliId = desktop.Where(d => d.CliSessionId is not null).ToDictionary(d => d.CliSessionId!, StringComparer.Ordinal);

        // Task name -> task id, learned from runs where both are known.
        var idByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in statuses.Values)
        {
            if (s.ScheduledTask is { } name && byCliId.TryGetValue(s.Id, out var d) && d.ScheduledTaskId is { } id)
            {
                idByName[name] = id;
            }
        }

        var runs = new List<(string TaskKey, string RunKey, DesktopSession? Desktop, SessionStatus? Status, DateTimeOffset? Created)>();
        var seenCliIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in desktop.Where(d => d.ScheduledTaskId is not null))
        {
            var status = d.CliSessionId is { } cli ? statuses.GetValueOrDefault(cli) : null;
            if (d.CliSessionId is { } seen)
            {
                seenCliIds.Add(seen);
            }

            runs.Add(("id:" + d.ScheduledTaskId, d.LocalId, d, status, d.CreatedAt ?? FromUnix(status?.Started)));
        }

        foreach (var s in statuses.Values.Where(s => s.ScheduledTask is not null && !seenCliIds.Contains(s.Id)))
        {
            var d = byCliId.GetValueOrDefault(s.Id);
            var taskKey = d?.ScheduledTaskId is { } id ? "id:" + id
                : idByName.TryGetValue(s.ScheduledTask!, out var learned) ? "id:" + learned
                : "name:" + s.ScheduledTask;
            runs.Add((taskKey, d?.LocalId ?? "hook_" + s.Id, d, s, d?.CreatedAt ?? FromUnix(s.Started)));
        }

        var result = new List<ScheduledRun>();
        foreach (var task in runs.Where(r => r.Desktop?.IsArchived != true).GroupBy(r => r.TaskKey))
        {
            var ordered = task.OrderByDescending(r => r.Created ?? DateTimeOffset.MinValue).ThenBy(r => r.RunKey, StringComparer.Ordinal).ToList();
            var newest = ordered[0];
            var title = GoodTitle(newest.Desktop?.Title)
                ?? ordered.Skip(1).Select(r => GoodTitle(r.Desktop?.Title)).FirstOrDefault(t => t is not null)
                ?? task.Select(r => r.Status?.ScheduledTask).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? fallbackTitle;

            // A run that is still working is not "unread" yet; one that waits for the user is.
            var running = newest.Status?.State == SessionState.Working;
            var unread = !running && !readStore.IsRead(newest.RunKey, newest.Created);
            result.Add(new ScheduledRun(task.Key, newest.RunKey, title, newest.Created, newest.Desktop, newest.Status, unread));
        }

        return result
            .OrderByDescending(r => r.Unread)
            .ThenByDescending(r => r.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.RunKey, StringComparer.Ordinal)
            .ToList();
    }

    private static string? GoodTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) || title.TrimStart().StartsWith(RawPromptPrefix, StringComparison.OrdinalIgnoreCase) ? null : title;

    private static DateTimeOffset? FromUnix(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;
}
