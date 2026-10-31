using SessionPulse.Core.Status;

namespace SessionPulse.Core.Tests;

/// <summary>
/// Parses every expected status file of the hook tests (tests/hook/cases/*/expected/*.json),
/// so hook and plugin cannot drift apart without a failing test.
/// </summary>
public class HookContractTests
{
    public static TheoryData<string> ExpectedFiles()
    {
        var data = new TheoryData<string>();
        var cases = Path.Combine(RepoRoot(), "tests", "hook", "cases");
        foreach (var file in Directory.EnumerateFiles(cases, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) == "expected")
            {
                data.Add(Path.GetRelativePath(cases, file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ExpectedFiles))]
    public void HookOutputParses(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), "tests", "hook", "cases", relativePath);

        var result = StatusFileReader.Read(path);

        Assert.True(result.Outcome == StatusParseOutcome.Ok, result.Error);
        var s = result.Status!;
        Assert.Equal(Path.GetFileNameWithoutExtension(path), s.Id);
        Assert.NotEqual(StatusDetail.Unknown, s.Detail);
        Assert.True(s.Updated >= s.Started, "updated before started");
        Assert.True(s.TasksDone <= s.TasksTotal, "more tasks done than total");
    }

    [Fact]
    public void FindsExpectedFiles() => Assert.True(ExpectedFiles().Count >= 20);

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("repository root (global.json) not found");
    }
}
