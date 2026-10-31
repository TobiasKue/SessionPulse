using System.Text;
using SessionPulse.Core.Status;

namespace SessionPulse.Core.Tests;

public class StatusFileParserTests
{
    private const string Full =
        """{"v":2,"id":"cowork-0001","src":"cowork","label":"Sag \"Hallo\" für","sched":"Daily digest","state":"working","detail":"tool","tool":"Bash","event":"PreToolUse","started":100,"turnStarted":110,"activity":120,"updated":130,"prompts":2,"tools":5,"tasksDone":1,"tasksTotal":3}""";

    private static StatusParseResult Parse(string json) => StatusFileParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ParsesAllFields()
    {
        var result = Parse(Full);

        Assert.Equal(StatusParseOutcome.Ok, result.Outcome);
        var s = result.Status!;
        Assert.Equal("cowork-0001", s.Id);
        Assert.Equal(SessionSource.Cowork, s.Source);
        Assert.Equal("Sag \"Hallo\" für", s.Label);
        Assert.Equal("Daily digest", s.ScheduledTask);
        Assert.Equal(SessionState.Working, s.State);
        Assert.Equal(StatusDetail.Tool, s.Detail);
        Assert.Equal("Bash", s.Tool);
        Assert.Equal("PreToolUse", s.Event);
        Assert.Equal((100L, 110L, 120L, 130L), (s.Started, s.TurnStarted, s.Activity, s.Updated));
        Assert.Equal((2, 5, 1, 3), (s.Prompts, s.Tools, s.TasksDone, s.TasksTotal));
    }

    [Fact]
    public void OptionalFieldsDefault()
    {
        var s = Parse("""{"v":2,"id":"a","state":"done"}""").Status!;

        Assert.Equal(SessionSource.Code, s.Source);
        Assert.Equal(string.Empty, s.Label);
        Assert.Null(s.ScheduledTask);
        Assert.Null(s.Tool);
        Assert.Equal(StatusDetail.Unknown, s.Detail);
        Assert.Equal(0, s.Prompts);
        Assert.True(s.IsFinished);
        Assert.Equal((null, null, 0, null), (s.Title, s.Host, s.ProcessId, s.Wsl));
        Assert.False(s.RunsOutsideDesktop);
    }

    [Fact]
    public void ParsesWhereTheSessionRuns()
    {
        var cli = Parse("""{"v":2,"id":"a","state":"done","title":"Fix \"login\"","host":"cli","pid":4242}""").Status!;
        var wsl = Parse("""{"v":2,"id":"b","state":"done","host":"cli","wsl":"Ubuntu"}""").Status!;
        var app = Parse("""{"v":2,"id":"c","state":"done","host":"claude-desktop","pid":-1}""").Status!;

        Assert.Equal(("Fix \"login\"", "cli", 4242, (string?)null), (cli.Title, cli.Host, cli.ProcessId, cli.Wsl));
        Assert.True(cli.RunsOutsideDesktop);
        Assert.Equal("Ubuntu", wsl.Wsl);
        Assert.False(app.RunsOutsideDesktop);
        Assert.Equal(0, app.ProcessId);
    }

    [Fact]
    public void IgnoresUnknownFieldsAndDetailCodes()
    {
        var result = Parse("""{"v":2,"id":"a","state":"idle","detail":"from-the-future","newField":{"x":1}}""");

        Assert.Equal(StatusParseOutcome.Ok, result.Outcome);
        Assert.Equal(StatusDetail.Unknown, result.Status!.Detail);
    }

    [Theory]
    [InlineData("""{"v":1,"id":"a","state":"idle"}""")]
    [InlineData("""{"v":3,"id":"a","state":"idle"}""")]
    public void RejectsOtherVersions(string json) =>
        Assert.Equal(StatusParseOutcome.UnsupportedVersion, Parse(json).Outcome);

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"v":2,"id":"a","state":"idle" """)]
    [InlineData("""[1,2]""")]
    [InlineData("""{"id":"a","state":"idle"}""")]
    [InlineData("""{"v":"2","id":"a","state":"idle"}""")]
    [InlineData("""{"v":2,"state":"idle"}""")]
    [InlineData("""{"v":2,"id":"a","state":"sleeping"}""")]
    public void RejectsInvalidFiles(string json) =>
        Assert.Equal(StatusParseOutcome.Invalid, Parse(json).Outcome);

    [Fact]
    public void ToleratesInvalidUtf8InLabel()
    {
        // "für" with the second byte of "ü" missing, as a byte-wise cut would produce.
        var bytes = Encoding.UTF8.GetBytes("""{"v":2,"id":"a","label":"f#r","state":"idle"}""");
        var i = Array.IndexOf(bytes, (byte)'#');
        bytes[i] = 0xC3;

        var result = StatusFileParser.Parse(bytes);

        Assert.Equal(StatusParseOutcome.Ok, result.Outcome);
        Assert.Equal("f�r", result.Status!.Label);
    }

    [Fact]
    public void NegativeAndHugeNumbersBecomeZero()
    {
        var s = Parse("""{"v":2,"id":"a","state":"idle","prompts":-3,"tools":99999999999,"started":-1}""").Status!;

        Assert.Equal((0, 0, 0L), (s.Prompts, s.Tools, s.Started));
    }

    [Fact]
    public void ReaderAllowsConcurrentReplace()
    {
        var dir = Directory.CreateTempSubdirectory("sessionpulse-test");
        try
        {
            var file = Path.Combine(dir.FullName, "a.json");
            var tmp = Path.Combine(dir.FullName, ".a.tmp");
            File.WriteAllText(file, Full);
            File.WriteAllText(tmp, Full.Replace("\"working\"", "\"done\""));

            // While the reader's sharing mode is in effect, an atomic replace must succeed.
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                ReplaceLikeTheHook(tmp, file);
            }

            Assert.Equal(SessionState.Done, StatusFileReader.Read(file).Status!.State);
            Assert.Equal(StatusParseOutcome.Invalid, StatusFileReader.Read(Path.Combine(dir.FullName, "missing.json")).Outcome);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Renames like the hook's <c>mv</c>. On Windows, File.Move (MoveFileEx) never replaces a file that is
    /// open, whatever its sharing mode, while Git Bash's mv uses POSIX rename semantics, which succeed as
    /// long as the reader shares Delete. So on Windows the test needs the real mv.
    /// </summary>
    private static void ReplaceLikeTheHook(string source, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(source, target, overwrite: true);
            return;
        }

        // Not "bash" from PATH: that can be WSL (docs/LESSONS.md).
        var bash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (!File.Exists(bash))
        {
            Assert.Skip("Git Bash not found at " + bash);
        }

        var psi = new System.Diagnostics.ProcessStartInfo(bash, ["-c", "mv -f \"$1\" \"$2\"", "mv", source, target])
        {
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        Assert.True(p.ExitCode == 0, "mv failed: " + error);
    }
}
