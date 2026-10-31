namespace Loupedeck.SessionPulsePlugin;

using SessionPulse.Core;
using SessionPulse.Core.Approvals;
using SessionPulse.Core.Config;
using SessionPulse.Core.Desktop;
using SessionPulse.Core.Dictation;
using SessionPulse.Core.Platform;
using SessionPulse.Core.Presentation;
using SessionPulse.Core.Scheduled;
using SessionPulse.Core.Sessions;

// Inside the plugin class, "PluginStatus" is the inherited property, not the enum.
using StatusKind = Loupedeck.PluginStatus;

/// <summary>
/// Universal, API-only plugin. Owns the one-second tick that feeds the <see cref="Dashboard"/> and tells the
/// service which keys to redraw. All dashboard access happens under <see cref="gate"/>.
/// </summary>
public class SessionPulsePlugin : Plugin
{
    public const int SessionKeyCount = 8;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConfigReloadInterval = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly SessionKeyCommand?[] sessionKeys = new SessionKeyCommand?[SessionKeyCount];
    private readonly List<SessionPulseFolder> folders = [];
    private Timer? timer;
    private SessionStore? store;
    private Dashboard? dashboard;
    private string configPath = string.Empty;
    private string dictationConfigPath = string.Empty;
    private DictationConfig? dictationConfig;
    private DictationCommand? dictationKey;
    private NextWaitingCommand? nextWaitingKey;
    private int nextWaitingShown = -1;
    private PowerShellScripts? scripts;
    private IScrollToTop scroller = new NoScrollToTop();
    private volatile bool dictating;
    private bool dictationConfiguredShown;
    private int dictationBusy;
    private SessionPulseConfig config = SessionPulseConfig.Default;
    private DateTimeOffset configLoaded = DateTimeOffset.MinValue;
    private IUriLauncher launcher = new ShellUriLauncher();
    private IWindowActivator activator = new NoWindowActivator();
    private StatusKind? reportedStatus;
    private int tickRunning;

    public SessionPulsePlugin()
    {
        PluginLog.Init(this.Log);
    }

    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    internal Diagnostics Diagnostics { get; } = new();

    internal KeyRenderer Renderer { get; } = new();

    /// <summary>Increases every tick; drives the spinner and the pulse.</summary>
    internal int Frame { get; private set; }

    internal SessionPulseConfig Config => this.config;

    internal Texts Texts { get; } = Texts.Current;

    internal PressGuard PressGuard { get; } = new(TimeProvider.System);

    internal int WaitingCount { get; private set; }

    internal (bool Recording, bool Configured) DictationState => (this.dictating, this.dictationConfig is not null);

    public override void Load()
    {
        var statusDir = StatusPaths.ResolveStatusDirectory();
        this.configPath = Path.Combine(statusDir, StatusPaths.ConfigFileName);
        this.dictationConfigPath = Path.Combine(statusDir, DictationConfig.FileName);
        this.ReloadConfigIfDue(force: true);

        var dataDir = this.GetPluginDataDirectory();
        IoHelpers.EnsureDirectoryExists(dataDir);

        if (OperatingSystem.IsWindows())
        {
            this.scripts = new PowerShellScripts(Path.Combine(Path.GetDirectoryName(this.AssemblyFilePath) ?? ".", "Scripts"));
            this.activator = new WindowsWindowActivator(PluginLog.Info, this.scripts);
            this.scroller = new WindowsScrollToTop(this.scripts);
        }

        this.store = new SessionStore(statusDir, TimeProvider.System, PluginLog.Info);
        this.store.StartWatching();
        var acks = new AckStore(Path.Combine(dataDir, "acked.json"), PluginLog.Warning);
        var desktopRoot = this.config.DesktopDataDirectory ?? DesktopSessionIndex.DefaultDataRoot(Environment.GetEnvironmentVariable);
        var desktop = new DesktopSessionIndex(desktopRoot, TimeProvider.System);
        var scheduledReads = new ScheduledReadStore(Path.Combine(dataDir, "scheduled-state.json"), TimeProvider.System, PluginLog.Warning);
        var approvals = new ApprovalStore(statusDir, TimeProvider.System, PluginLog.Warning);
        // No UsageStore: the usage limits key and its haptic event are hidden for now (the status line
        // delivered no data from Claude Desktop's Code tab); Core and the status line helper still support it.
        this.dashboard = new Dashboard(this.store, acks, desktop, TimeProvider.System, () => this.config, this.Texts, SessionKeyCount, scheduledReads, approvals);

        PluginLog.Info($"SessionPulse loaded: status dir '{statusDir}', Claude Desktop data '{desktopRoot ?? "(none)"}', data dir '{dataDir}'");
        this.timer = new Timer(_ => this.OnTick(), null, TimeSpan.Zero, TickInterval);
    }

    public override void Unload()
    {
        this.timer?.Dispose();
        this.timer = null;
        this.store?.Dispose();
    }

    internal void Register(SessionKeyCommand key)
    {
        lock (this.gate)
        {
            this.sessionKeys[key.Number - 1] = key;
        }
    }

    internal void RegisterNextWaiting(NextWaitingCommand command)
    {
        lock (this.gate)
        {
            this.nextWaitingKey = command;
        }
    }

    internal void OpenNextWaiting()
    {
        if (this.PressGuard.ShouldIgnore())
        {
            return;
        }

        this.Diagnostics.CountPress();
        lock (this.gate)
        {
            if (this.dashboard?.PressNextWaiting(this.launcher, this.activator) != true)
            {
                this.LogVerbose("next waiting: nothing waits");
            }
        }

        this.RequestTick();
    }

    internal void OpenClaude()
    {
        if (this.PressGuard.ShouldIgnore())
        {
            return;
        }

        this.Diagnostics.CountPress();
        this.launcher.Open(SessionPulse.Core.Desktop.DeepLinks.App);
        this.activator.BringClaudeToFront();
    }

    internal void DecideApproval(string requestId, ApprovalDecision decision)
    {
        bool done;
        lock (this.gate)
        {
            done = this.dashboard?.DecideApproval(requestId, decision) ?? false;
        }

        this.LogVerbose($"approval {decision}: {(done ? "sent" : "request no longer open")}");
        this.RequestTick();
    }

    internal void RegisterDictation(DictationCommand command)
    {
        lock (this.gate)
        {
            this.dictationKey = command;
        }
    }

    internal void RegisterFolder(SessionPulseFolder folder)
    {
        lock (this.gate)
        {
            this.folders.Add(folder);
        }
    }

    internal void LogVerbose(string text)
    {
        if (this.config.Verbose)
        {
            PluginLog.Verbose(text);
        }
    }

    /// <summary>Runs a tick soon, e.g. when a folder opens and needs its content.</summary>
    internal void RequestTick() => ThreadPool.QueueUserWorkItem(_ => this.OnTick());

    internal KeyModel GetKey(int number)
    {
        lock (this.gate)
        {
            return this.dashboard?.Keys[number - 1] ?? KeyModel.Empty(number);
        }
    }

    internal void OnSessionKeyPressed(int number)
    {
        if (this.PressGuard.ShouldIgnore())
        {
            this.LogVerbose($"ghost press on key {number} ignored");
            return;
        }

        this.Diagnostics.CountPress();
        bool handled;
        lock (this.gate)
        {
            handled = this.dashboard?.PressKey(number, this.launcher, this.activator) ?? false;
        }

        this.LogVerbose($"key {number} pressed ({(handled ? "session opened" : "empty")})");
        this.OnTick();
    }

    /// <summary>Opens a session picked in a folder (and acknowledges it if finished).</summary>
    internal void OpenSession(string sessionId)
    {
        lock (this.gate)
        {
            this.dashboard?.PressSession(sessionId, this.launcher, this.activator);
        }

        this.RequestTick();
    }

    internal void OpenScheduledRun(string runKey)
    {
        lock (this.gate)
        {
            this.dashboard?.PressScheduledRun(runKey, this.launcher, this.activator, this.scroller);
        }

        this.RequestTick();
    }

    /// <summary>Starts or stops dictation (see <see cref="DictationCommand"/>). Runs in the background.</summary>
    internal void ToggleDictation()
    {
        if (this.PressGuard.ShouldIgnore() || Interlocked.Exchange(ref this.dictationBusy, 1) == 1)
        {
            return;
        }

        this.Diagnostics.CountPress();
        _ = Task.Run(async () =>
        {
            try
            {
                var config = this.dictationConfig;
                if (config is null)
                {
                    PluginLog.Info("dictation is not set up: create dictation.json (docs/configuration.md)");
                    return;
                }

                if (this.scripts is null || !OperatingSystem.IsWindows())
                {
                    PluginLog.Info("dictation is only available on Windows");
                    return;
                }

                var start = !this.dictating;

                // The configured chat must be in front; the script refuses to dictate into another one.
                this.launcher.Open(config.ChatLink);
                this.activator.BringClaudeToFront();
                var result = await this.scripts.RunAsync("dictation.ps1", "-ChatId", config.ChatId, "-ButtonNames", start ? config.StartButtons : config.StopButtons).ConfigureAwait(false);
                if (result.StartsWith("OK", StringComparison.Ordinal))
                {
                    this.dictating = start;
                    this.LogVerbose($"dictation {(start ? "started" : "stopped")}: {result}");
                }
                else
                {
                    PluginLog.Warning($"dictation {(start ? "start" : "stop")} failed: {result}");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "dictation failed");
            }
            finally
            {
                Volatile.Write(ref this.dictationBusy, 0);
                this.dictationKey?.Redraw();
            }
        });
    }

    internal void AcknowledgeAllFinished()
    {
        int count;
        lock (this.gate)
        {
            count = this.dashboard?.AcknowledgeAllFinished() ?? 0;
        }

        this.LogVerbose($"acknowledged {count} finished sessions");
        this.RequestTick();
    }

    private void OnTick()
    {
        // The timer and key presses can overlap; one tick at a time is enough.
        if (Interlocked.Exchange(ref this.tickRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var redraw = new List<SessionKeyCommand>();
            var folderUpdates = new List<(SessionPulseFolder Folder, FolderUpdate Update)>();
            DictationCommand? redrawDictation = null;
            var redrawCommands = new List<Action>();
            IReadOnlyList<PulseEvent> pulses = [];
            bool hookSeen;
            lock (this.gate)
            {
                if (this.dashboard is null)
                {
                    return;
                }

                this.ReloadConfigIfDue(force: false);
                this.Frame++;
                var changed = this.dashboard.Tick();
                foreach (var key in this.dashboard.Keys)
                {
                    if ((key.IsAnimated || changed.Contains(key.Number)) && this.sessionKeys[key.Number - 1] is { } command)
                    {
                        redraw.Add(command);
                    }
                }

                foreach (var folder in this.folders)
                {
                    folderUpdates.Add((folder, folder.Prepare(this.dashboard, this.Texts)));
                }

                pulses = this.dashboard.TakeEvents();
                this.WaitingCount = this.dashboard.Counts.Waiting;
                if (this.nextWaitingKey is { } next && this.WaitingCount != this.nextWaitingShown)
                {
                    this.nextWaitingShown = this.WaitingCount;
                    redrawCommands.Add(next.Redraw);
                }

                var configured = this.dictationConfig is not null;
                if (this.dictating || configured != this.dictationConfiguredShown)
                {
                    this.dictationConfiguredShown = configured;
                    redrawDictation = this.dictationKey;
                }

                hookSeen = this.store!.HookSeen;
                this.Diagnostics.MaybeLogSummary(this.dashboard, this.Config.Verbose);
            }

            foreach (var command in redraw)
            {
                command.Redraw();
            }

            foreach (var (folder, update) in folderUpdates)
            {
                folder.Notify(update);
            }

            redrawDictation?.Redraw();
            foreach (var redrawCommand in redrawCommands)
            {
                redrawCommand();
            }

            this.RaiseHaptics(pulses);

            this.UpdatePluginStatus(hookSeen);
        }
        catch (Exception ex)
        {
            this.Diagnostics.LogTickError(ex);
        }
        finally
        {
            Volatile.Write(ref this.tickRunning, 0);
        }
    }

    /// <summary>Haptic feedback on the MX Master 4 (events/DefaultEventSource.yaml). Off with HAPTICS=0.</summary>
    private void RaiseHaptics(IReadOnlyList<PulseEvent> pulses)
    {
        if (pulses.Count == 0 || !this.config.Haptics)
        {
            return;
        }

        foreach (var pulse in pulses)
        {
            try
            {
                this.PluginEvents.RaiseEvent(Dashboard.EventName(pulse));
                this.LogVerbose("haptic event " + Dashboard.EventName(pulse));
            }
            catch (Exception ex)
            {
                this.Diagnostics.LogTickError(ex);
            }
        }
    }

    private void ReloadConfigIfDue(bool force)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - this.configLoaded < ConfigReloadInterval)
        {
            return;
        }

        this.configLoaded = now;
        this.config = SessionPulseConfig.Load(this.configPath);
        this.dictationConfig = DictationConfig.Load(this.dictationConfigPath, PluginLog.Warning);
    }

    private void UpdatePluginStatus(bool hookSeen)
    {
        var status = hookSeen ? StatusKind.Normal : StatusKind.Warning;
        if (status == this.reportedStatus)
        {
            return;
        }

        this.reportedStatus = status;
        if (hookSeen)
        {
            this.OnPluginStatusChanged(StatusKind.Normal, null);
        }
        else
        {
            this.OnPluginStatusChanged(StatusKind.Warning, "No session status yet. Install the SessionPulse plugin in Claude and send a prompt.");
        }
    }
}
