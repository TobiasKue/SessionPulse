using System.Diagnostics;
using SessionPulse.Core.Config;

namespace SessionPulse.Core.Tests;

public class StatusPathsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void ExplicitDirectoryWins() =>
        Assert.Equal("/custom", StatusPaths.ResolveStatusDirectory(HostPlatform.Linux, Env(("SESSIONPULSE_DIR", "/custom"), ("HOME", "/home/u"))));

    [Fact]
    public void WindowsUsesUserProfileNotAppData() =>
        Assert.Equal(
            Path.Combine(@"C:\Users\u", ".sessionpulse"),
            StatusPaths.ResolveStatusDirectory(HostPlatform.Windows, Env(("USERPROFILE", @"C:\Users\u"), ("LOCALAPPDATA", @"C:\Users\u\AppData\Local"))));

    [Fact]
    public void MacUsesApplicationSupport() =>
        Assert.Equal(
            Path.Combine("/Users/u", "Library", "Application Support", "SessionPulse"),
            StatusPaths.ResolveStatusDirectory(HostPlatform.MacOS, Env(("HOME", "/Users/u"))));

    [Fact]
    public void LinuxUsesXdgStateHome()
    {
        Assert.Equal(Path.Combine("/xdg", "sessionpulse"), StatusPaths.ResolveStatusDirectory(HostPlatform.Linux, Env(("XDG_STATE_HOME", "/xdg"), ("HOME", "/home/u"))));
        Assert.Equal(Path.Combine("/home/u", ".local", "state", "sessionpulse"), StatusPaths.ResolveStatusDirectory(HostPlatform.Linux, Env(("HOME", "/home/u"))));
    }

    /// <summary>Runs the real hook and checks that it writes where the plugin will look.</summary>
    [Fact]
    public void HookWritesToTheSameDefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // "bash" may resolve to WSL here; the Windows default (USERPROFILE\.sessionpulse) is checked by
            // tests/hook/run.sh, which simulates Windows by setting LOCALAPPDATA.
            return;
        }

        var root = Directory.CreateTempSubdirectory("sessionpulse-paths");
        try
        {
            var home = Path.Combine(root.FullName, "home");
            Directory.CreateDirectory(home);
            var hook = Path.Combine(HookContractTests.RepoRoot(), "claude-plugin", "hooks", "scripts", "status.sh");
            var psi = new ProcessStartInfo("bash", [hook])
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment.Remove("SESSIONPULSE_DIR");
            psi.Environment.Remove("XDG_STATE_HOME");
            psi.Environment.Remove("LOCALAPPDATA");
            psi.Environment["HOME"] = home;

            using (var p = Process.Start(psi)!)
            {
                p.StandardInput.Write("""{"session_id":"paths-1","cwd":"/tmp/x","hook_event_name":"Stop"}""");
                p.StandardInput.Close();
                p.WaitForExit(10_000);
                Assert.Equal(0, p.ExitCode);
            }

            var platform = OperatingSystem.IsMacOS() ? HostPlatform.MacOS : HostPlatform.Linux;
            var expected = StatusPaths.ResolveStatusDirectory(platform, Env(("HOME", home)));
            Assert.True(File.Exists(Path.Combine(expected, "sessions", "paths-1.json")), $"no status file below {expected}");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
