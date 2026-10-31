# Architecture

```
Claude (Cowork / Claude Code in Claude Desktop)
   │  hook events (JSON on stdin)
   ▼
Claude plugin "sessionpulse" ── status.sh (bash) ──► <status dir>/sessions/<id>.json
                                                         │  FileSystemWatcher + polling
                                                         ▼
                                  Logi plugin "SessionPulse" (C#, runs in Logi Plugin Service)
                                                         │  key images, folders, key presses
                                                         ▼
                                                   MX Creative Keypad ──► claude:// deep link opens the session
```

The two halves share nothing but the [status file format](status-format.md),
so each can be developed, tested and released on its own.

## Components

| Path | What | Depends on |
|---|---|---|
| `claude-plugin/` | Claude plugin: manifest, `hooks/hooks.json`, `hooks/scripts/status.sh` | bash ≥ 4.4 (Git Bash on Windows), `mv`, `cat`, `mkdir` |
| `src/SessionPulse.Core/` | Everything that can be tested without a device: status parsing, configuration, paths, the session store, acknowledgements, key assignment, Claude Desktop metadata, deep links, key models and texts (`Dashboard` ties them together) | .NET 10 only |
| `src/SessionPulsePlugin/` | The Logi plugin: plugin class with the one-second tick, the eight session key actions, the key renderer, Windows window activation | `LogiPluginApi`, Core |
| `tools/KeyPreview/` | Renders all key states to PNG with the SDK's drawing code (development and CI smoke test) | `LogiPluginApi` runtime, SkiaSharp |
| `src/SessionPulsePlugin/Scripts/` | UI Automation helpers (scroll to top, dictation, terminal tab), shipped as `bin/Scripts/*.ps1` | Windows PowerShell |
| `tests/hook/` | Golden tests for the hook: payload sequence → expected status file | bash |
| `tests/SessionPulse.Core.Tests/` | Unit tests and a contract test that parses every expected hook output | xUnit v3 |

## Decisions

* **Files, not IPC.** The hook must not depend on the plugin running, and
  must never block Claude. A file that is atomically replaced is the simplest
  thing that works in both directions of failure.
* **Synchronous hook.** Claude Code can run hooks with `async: true`, but async
  runs have no ordering guarantee: a `PostToolUse` could finish before its
  `PreToolUse` and leave a wrong state. The hook is fast enough to run
  synchronously (a few ms on Linux; Windows numbers come from CI).
* **No locking.** Parallel tool calls can race on the read-modify-write.
  The worst case is a lost counter increment, which is harmless (see `tools`
  in the format). Locking would need extra processes on every call.
* **Bash, not PowerShell.** Claude runs hooks in Git Bash on Windows, and the
  same script works on macOS and Linux. Only builtins on the hot path; process
  creation is the expensive part in Git Bash.
* **Own build rules for the plugin project.** It starts from the
  `logiplugintool generate` template but pins `LogiPluginApi`, copies only our
  own assemblies, writes the package version into `LoupedeckPackage.yaml` and
  only creates the development `.link` file on local Windows builds.
* **Logic outside the plugin project.** `SessionPulse.Core` has no reference
  to the Logi Plugin API, so CI and contributors on any OS can build and test
  it. The plugin project stays a thin adapter.
* **Platform behind interfaces.** Paths, window focus, opening URIs and UI
  Automation live behind interfaces in Core with a Windows implementation in
  the plugin, so macOS can be added later.
* **Target framework net10.0.** Logi Plugin Service 6.4 hosts .NET 10
  (`LogiPluginApi` 6.4.2.3567 release notes: "Targets .NET 10 and requires Logi
  Plugin Service 6.4 or later at runtime"). Code loaded into the service must
  not target a newer runtime.

## Keypad plugin

* **One tick per second.** The plugin's timer calls `Dashboard.Tick()`, which
  re-reads status files when the file watcher reported a change or every 5 s,
  and Claude Desktop metadata at most every 15 s. It returns the keys whose
  model changed; the plugin redraws those plus all animated keys (spinner,
  pulse). A key model is a value object, so equality is the redraw signature.
* **Stable keys.** A session keeps its key while it is visible. Free keys are
  filled by importance (waiting, working, done, idle) and recency. When all
  keys are taken, a waiting or working session replaces the least important
  done or idle one.
* **Visibility.** Hidden are: acknowledged finished sessions, archived
  sessions, and sessions without any prompt that are not running (Cowork
  creates those just by opening a session).
* **Pressing a key** opens the session through its `claude://` deep link (or
  just the app if the session is unknown to Claude Desktop), brings the Claude
  window to the front and acknowledges the session if it is finished. Sessions
  of a Claude Code outside the app (`host` other than `claude-desktop`) never
  open Claude Desktop. `TerminalWindowFinder` (Core, testable) walks from the
  session's process (`pid`) via its console host and ancestors to a terminal
  window; if that is exact (a console window, VS Code, or the only Windows
  Terminal window) the activator shows it right away. Then
  `Scripts/show-terminal.ps1` finds the exact window (it attaches to the
  session's console: `GetConsoleWindow()`, whose root owner is the terminal
  window) and selects the Windows Terminal tab whose name is the session
  `title` without Claude Code's status glyph (`TerminalTabs`, same rule in
  Core and script, checked by a test); the plugin brings the window it reports
  to the front. WSL sessions report no usable process id, so for them the tab
  search runs, and if it finds nothing (no title yet, or the session is in a
  pane without focus) the first visible window with a WSL console comes up.
* **Titles**: Claude Desktop's title, else the `title` the hook read from the
  transcript (CLI sessions), else the hook's label.
* **WSL**: the hook detects `WSL_DISTRO_NAME` and writes to the Windows status
  directory (asked from `cmd.exe` once, cached), so the plugin needs no WSL
  access.
* **Folders** (working / waiting / done / scheduled) share one base class: a
  tile with badge and count, a page of up to eight keys ("back" is added by the
  service). After a press the folder closes with a delay and arms the
  ghost-press guard (`PressGuard`, docs/LESSONS.md).
* **Scheduled tasks** merge Claude Desktop metadata (`scheduledTaskId`) with the
  hook's task name (`sched`), keep the newest non-archived run per task and a
  read state of their own (`ScheduledRuns`, `ScheduledReadStore`).
* **Approvals** are a file handshake too (docs/status-format.md): only for
  `PermissionRequest`, only with `APPROVALS=1`, with a timeout after which
  Claude's own dialog takes over. This is the one place where the hook waits
  and writes to stdout. No network port is opened, unlike most similar tools.
* **Usage limits** come from Claude Code's status line input; a small helper
  writes `usage.json` and can chain the user's previous status line. The
  keypad key for it is hidden for now (the plugin creates no `UsageStore`).
* **Haptics** are Logi plugin events (`package/events/`); the `Dashboard`
  reports state changes, never the state found at start-up.
* **Scroll to top and dictation** use Windows UI Automation through small
  PowerShell scripts; the plugin reads only their first output line, with a
  timeout.
* **Drawing**: a dark key with a round, two-tone state badge (SVG, cached) as
  the main signal, title text in the service's default font, and a reason pill
  or progress bar at the bottom. Sizes are relative to the key; the title font
  shrinks until no word has to be broken.

## Possible optimizations

* The hook command is `bash "<path>"` in shell form, i.e. Git Bash starts a
  second bash. Sourcing the script in the outer shell would save one process
  start on Windows, but on macOS/Linux shell form uses `sh`. Measure first.
