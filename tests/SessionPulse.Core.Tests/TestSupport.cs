using System.Text.Json;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Tests;

internal sealed class ManualTime : TimeProvider
{
    public ManualTime(long unixSeconds) => this.Now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    public DateTimeOffset Now { get; set; }

    public long UnixNow => this.Now.ToUnixTimeSeconds();

    public override DateTimeOffset GetUtcNow() => this.Now;

    public void Advance(TimeSpan by) => this.Now += by;
}

internal sealed class TempDir : IDisposable
{
    public TempDir() => this.Path = Directory.CreateTempSubdirectory("sessionpulse-test").FullName;

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([this.Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class StatusFiles
{
    public static SessionStatus Make(
        string id,
        SessionState state = SessionState.Working,
        int prompts = 1,
        int tools = 0,
        long started = 1000,
        long turnStarted = 1000,
        long activity = 1000,
        long updated = 1000,
        string label = "demo",
        SessionSource source = SessionSource.Code,
        StatusDetail detail = StatusDetail.Thinking,
        string? tool = null,
        int tasksDone = 0,
        int tasksTotal = 0,
        string? sched = null,
        string? title = null,
        string? host = null,
        int pid = 0,
        string? wsl = null) =>
        new(id, source, label, sched, state, detail, tool, "Test", started, turnStarted, activity, updated, prompts, tools, tasksDone, tasksTotal, title, host, pid, wsl);

    /// <summary>Serializes like the hook does (field names and enum spellings of format v2).</summary>
    public static string ToJson(SessionStatus s)
    {
        var o = new Dictionary<string, object?>
        {
            ["v"] = 2,
            ["id"] = s.Id,
            ["src"] = s.Source == SessionSource.Cowork ? "cowork" : "code",
            ["label"] = s.Label,
            ["state"] = s.State.ToString().ToLowerInvariant(),
            ["detail"] = s.Detail.ToString().ToLowerInvariant(),
            ["event"] = s.Event,
            ["started"] = s.Started,
            ["turnStarted"] = s.TurnStarted,
            ["activity"] = s.Activity,
            ["updated"] = s.Updated,
            ["prompts"] = s.Prompts,
            ["tools"] = s.Tools,
            ["tasksDone"] = s.TasksDone,
            ["tasksTotal"] = s.TasksTotal,
        };
        if (s.ScheduledTask is not null)
        {
            o["sched"] = s.ScheduledTask;
        }

        if (s.Tool is not null)
        {
            o["tool"] = s.Tool;
        }

        if (s.Title is not null)
        {
            o["title"] = s.Title;
        }

        if (s.Host is not null)
        {
            o["host"] = s.Host;
        }

        if (s.ProcessId > 0)
        {
            o["pid"] = s.ProcessId;
        }

        if (s.Wsl is not null)
        {
            o["wsl"] = s.Wsl;
        }

        return JsonSerializer.Serialize(o) + "\n";
    }

    public static void Write(string statusDir, SessionStatus s)
    {
        var dir = Path.Combine(statusDir, "sessions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, s.Id + ".json"), ToJson(s));
    }
}
