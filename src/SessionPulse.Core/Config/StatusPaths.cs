namespace SessionPulse.Core.Config;

public enum HostPlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// Locates the status directory. Must compute the same default as sp_status_dir in
/// claude-plugin/hooks/scripts/status.sh; both sides are covered by tests.
/// </summary>
public static class StatusPaths
{
    public const string DirectoryVariable = "SESSIONPULSE_DIR";
    public const string ConfigFileName = "sessionpulse.conf";
    public const string SessionsFolderName = "sessions";

    public static HostPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? HostPlatform.Windows
        : OperatingSystem.IsMacOS() ? HostPlatform.MacOS
        : HostPlatform.Linux;

    public static string ResolveStatusDirectory() =>
        ResolveStatusDirectory(CurrentPlatform, Environment.GetEnvironmentVariable);

    public static string ResolveStatusDirectory(HostPlatform platform, Func<string, string?> getEnvironmentVariable)
    {
        var configured = getEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        switch (platform)
        {
            case HostPlatform.Windows:
                // Not below AppData: the Store (MSIX) version of Claude Desktop redirects files that its hooks
                // create there into its package folder, where this plugin would not see them (docs/LESSONS.md).
                return Path.Combine(getEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sessionpulse");
            case HostPlatform.MacOS:
                return Path.Combine(Home(getEnvironmentVariable), "Library", "Application Support", "SessionPulse");
            default:
                var state = getEnvironmentVariable("XDG_STATE_HOME");
                if (string.IsNullOrEmpty(state))
                {
                    state = Path.Combine(Home(getEnvironmentVariable), ".local", "state");
                }

                return Path.Combine(state, "sessionpulse");
        }
    }

    private static string Home(Func<string, string?> getEnvironmentVariable) =>
        getEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
