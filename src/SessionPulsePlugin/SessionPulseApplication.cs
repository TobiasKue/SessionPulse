namespace Loupedeck.SessionPulsePlugin;

/// <summary>
/// Required by the SDK (every plugin has a ClientApplication class). SessionPulse is a universal plugin
/// that doesn't follow a foreground application, so this stays empty.
/// </summary>
public class SessionPulseApplication : ClientApplication
{
    protected override string GetProcessName() => string.Empty;

    protected override string GetBundleName() => string.Empty;

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
