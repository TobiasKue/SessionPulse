using System.Text;
using SessionPulse.Core.Desktop;

namespace SessionPulse.Core.Tests;

public class DesktopTests
{
    private static DesktopSession? Parse(string json, string localId = "local_1", DesktopSessionKind kind = DesktopSessionKind.Cowork) =>
        DesktopMetadataReader.Parse(Encoding.UTF8.GetBytes(json), localId, kind);

    [Fact]
    public void ReadsAllowedFields()
    {
        var s = Parse("""
            {"sessionId":"local_1","cliSessionId":"cli-1","title":" Weekly report ","isArchived":true,
             "createdAt":1761230000000,"scheduledTaskId":"task-9","other":{"nested":[1,2,{"x":"y"}]}}
            """)!;

        Assert.Equal(DesktopSessionKind.Cowork, s.Kind);
        Assert.Equal("local_1", s.LocalId);
        Assert.Equal("cli-1", s.CliSessionId);
        Assert.Equal("Weekly report", s.Title);
        Assert.True(s.IsArchived);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761230000000), s.CreatedAt);
        Assert.Equal("task-9", s.ScheduledTaskId);
    }

    [Fact]
    public void StopsAtSystemPrompt()
    {
        var s = Parse("""
            {"cliSessionId":"cli-1","systemPrompt":"secret","title":"after the prompt","email":"me@example.com"}
            """)!;

        Assert.Equal("cli-1", s.CliSessionId);
        Assert.Null(s.Title);
    }

    [Fact]
    public void FieldsBeyondTheReadLimitAreMissing()
    {
        using var dir = new TempDir();
        var path = dir.Combine("local_2.json");
        var big = new string('x', DesktopMetadataReader.MaxBytes);
        File.WriteAllText(path, $$"""{"cliSessionId":"cli-2","title":"T","messages":["{{big}}"],"scheduledTaskId":"late"}""");

        var s = DesktopMetadataReader.Read(path, DesktopSessionKind.Code)!;

        Assert.Equal("cli-2", s.CliSessionId);
        Assert.Equal("T", s.Title);
        Assert.Null(s.ScheduledTaskId);
        Assert.Equal("local_2", s.LocalId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"title":"no ids"}""")]
    [InlineData("garbage")]
    public void UnusableFilesGiveNull(string json) => Assert.Null(Parse(json));

    [Fact]
    public void ParsesIsoTimestamps()
    {
        var s = Parse("""{"cliSessionId":"c","createdAt":"2026-10-23T10:00:00Z"}""")!;

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero), s.CreatedAt);
    }

    [Fact]
    public void BuildsDeepLinks()
    {
        Assert.Equal("claude://claude.ai/cowork/local_abc", DeepLinks.ForSession(DesktopSessionKind.Cowork, "local_abc"));
        Assert.Equal("claude://code/continue?session=local_abc", DeepLinks.ForSession(DesktopSessionKind.Code, "local_abc"));
    }

    [Fact]
    public void IndexScansBothKindsAndRescansLazily()
    {
        using var dir = new TempDir();
        var time = new ManualTime(1000);
        var cowork = dir.Combine("claude_1", "local-agent-mode-sessions", "acc", "org");
        var code = dir.Combine("claude_1", "claude-code-sessions", "acc", "org");
        Directory.CreateDirectory(cowork);
        Directory.CreateDirectory(code);
        File.WriteAllText(Path.Combine(cowork, "local_c1.json"), """{"sessionId":"local_c1","cliSessionId":"hook-1","title":"Cowork task"}""");
        File.WriteAllText(Path.Combine(code, "local_k1.json"), """{"sessionId":"local_k1","cliSessionId":"hook-2","title":"Code task"}""");
        File.WriteAllText(Path.Combine(code, "unrelated.json"), """{"cliSessionId":"hook-3"}""");
        var index = new DesktopSessionIndex(dir.Path, time);

        Assert.True(index.RescanIfDue());
        Assert.Equal("claude://claude.ai/cowork/local_c1", index.Find("hook-1")!.DeepLink);
        Assert.Equal("claude://code/continue?session=local_k1", index.Find("hook-2")!.DeepLink);
        Assert.Null(index.Find("hook-3"));

        File.WriteAllText(Path.Combine(code, "local_k1.json"), """{"sessionId":"local_k1","cliSessionId":"hook-2","title":"Renamed"}""");
        File.SetLastWriteTimeUtc(Path.Combine(code, "local_k1.json"), DateTime.UtcNow.AddMinutes(1));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(index.RescanIfDue());
        Assert.Equal("Code task", index.Find("hook-2")!.Title);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(index.RescanIfDue());
        Assert.Equal("Renamed", index.Find("hook-2")!.Title);

        File.Delete(Path.Combine(cowork, "local_c1.json"));
        Assert.True(index.RescanIfDue(force: true));
        Assert.Null(index.Find("hook-1"));
    }

    [Fact]
    public void IndexWithoutDataRootIsEmpty()
    {
        var index = new DesktopSessionIndex(null, new ManualTime(0));

        Assert.False(index.RescanIfDue());
        Assert.Empty(index.Sessions);
    }
}
