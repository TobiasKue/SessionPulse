# Lessons learned

Things that are not (or not obviously) in any official documentation and that
cost real time. Each entry says where it comes from:

* **[prototype]**: observed in practice with the first, private prototype.
* **[docs]**: found in the official documentation; the page is cited.
* **[tests]**: found while writing the tests in this repository.
* **[device]**: found while setting up the first test on real hardware.

Please add to this file when you find something new.

## Hooks / Claude

1. **[prototype] Cowork runs hooks on the Windows host in Git Bash**, not in
   the Linux sandbox. Paths such as `/tmp` inside the sandbox are invisible to
   the hook. **Outdated (2026-10, see 46):** Cowork is now part of the chat and
   its hooks no longer run on this computer at all.
2. **[prototype] Never search the file system in a hook.** An early version
   located its target folder with `find /`, which put several seconds between
   `PreToolUse` and `PostToolUse`.
3. **[prototype] Cowork sends `SessionEnd` after every answer**, although the
   session goes on. Treat `ended` like `done`; don't hide the session.
4. **[prototype] Cowork sends `SessionStart`/`SessionEnd` when a session is
   merely opened or switched to.** So `updated` is not a measure of activity.
   Acknowledge by `[prompts, tools]`, judge liveness by `activity`.
5. **[prototype] The hook `session_id` is not the Claude Desktop session id.**
   They are linked through the `cliSessionId` field of the Desktop metadata.
6. **[prototype] Plain chats send no hooks** and therefore never appear.
7. **[prototype] Titles of scheduled runs**: Claude assigns the title only
   during the run. Until then the label is `<scheduled-task name='…`. The hook
   therefore extracts the task name into `sched`, and the plugin can reuse the
   title of an earlier run of the same task.
8. **[prototype] `scheduledTaskId` is not always within the first 64 KB** of
   the Desktop metadata, because large fields come before it. The hook's
   `sched` is the second source.
9. **[prototype] The Desktop metadata has no run state and no read state.**
   Take the run state from the status file (`working` does not count as
   unread) and keep the read state yourself.
10. **[docs] Async hooks have no ordering.** `async: true` would make the hook
    free for Claude, but runs can finish out of order, so a `PostToolUse` could
    overwrite the state of a later `PreToolUse`. Keep the hook synchronous and
    fast. (Claude Code hooks reference, "Run hooks in the background")
11. **[docs] `PermissionRequest` fires immediately**, whereas the
    `permission_prompt` notification only comes after the prompt has waited
    about six seconds. Use both. (Claude Code hooks reference,
    "PermissionRequest", "Notification")
12. **[docs] `SessionStart` and `UserPromptSubmit` carry `session_title`** when
    the user set a custom title. Generated titles are not included.
    (Claude Code hooks reference, "SessionStart input")
13. **[tests] Drain stdin.** A hook that exits without reading all of stdin
    makes the writer get `SIGPIPE`/`EPIPE` on large payloads. Read the first
    few KB with the `read` builtin and discard the rest with `cat`, which only
    starts a process when the payload is large.
    `tests/hook/run.sh` checks this with a 1 MB payload.
14. **[tests] Atomic replace on Windows needs cooperative readers.** Renaming
    over a file fails while another process has it open without
    `FILE_SHARE_DELETE`. Readers must open status files with
    `FileShare.ReadWrite | FileShare.Delete`; the hook retries the `mv` once.
    Git Bash's `mv` (POSIX rename semantics) then replaces an open file, but
    .NET's `File.Move` (MoveFileEx) never does, so tests must use the real `mv`.

## Logi Plugin Service / Options+

15. **[prototype] `RunCommand` fires on key down.** If a folder is closed right
    away, the key *release* lands on the main page and triggers the key at the
    same position ("ghost press"). Fix: close only 350 ms after the press, and
    ignore presses until 700 ms after a programmatic close.
16. **[docs] A dynamic folder closes by itself** "when the active application
    is changed". Bringing Claude to the front from a folder may therefore
    close it without our help, which is probably related to 15. The docs also
    recommend to either always close after a command or never.
    (Logi Actions SDK, `csharp/plugin-features/implementing-dynamic-folders.md`)
17. **[prototype] Parameterized actions (sub-entries) are unreliable**: with
    names, Options+ hides the sub-entries; without names, `ActionImageChanged`
    does not arrive. Fix: one class per variant and `ActionImageChanged()`
    without parameters.
18. **[prototype] Redraw a folder tile** with
    `Plugin.OnActionImageChanged("#DynamicFolder", "DynamicFolder#" + GetType().FullName, false)`.
19. **[prototype] Without an own icon template** the image uses only ~65–70 %
    of the key and the name is printed below. Fix: ship
    `metadata/DefaultIconTemplate.ict` with an image item `0,0,100,100`,
    `isFullScreen: true`, and a text item with `isVisible: false`.
    (Logi Actions SDK, `csharp/icons/icon-templates.md`)
20. **[prototype] Frozen key images**: every change in the Options+ icon
    editor creates
    `…\Applications\<device>\<app>\Profiles\<id>\ActionIcons\$<Plugin>___<Action>.ict`
    with an **embedded PNG**. It has the highest precedence, and the service no
    longer asks the plugin for an image. Fix: "reset to default" in the icon
    editor, or delete the file and restart the service. The docs confirm the
    precedence (user > action > plugin > global).
    (Logi Actions SDK, `csharp/icons/icon-templates.md`)
21. **[prototype] The development `.link` file** written by the build collides
    with an installed `.lplug4` ("already loaded"). For normal use always
    install the `.lplug4` and remove the `.link`. "already loaded" after a
    service restart is otherwise harmless.
22. **[prototype] The `.lplug4` installer dialog likes to hide behind other
    windows.** If it is not confirmed, the old version stays installed. Check
    the log ("Plugin '…' version '…' loaded") or
    `Plugins\<Name>\metadata\LoupedeckPackage.yaml`. The service must be
    running during installation.
23. **[prototype] Use a unique version for every build**, otherwise Options+
    may show cached images. Generate the version from the `.csproj` instead of
    patching `LoupedeckPackage.yaml` (which exists in the source **and** in
    `bin\Release`) afterwards.
24. **[docs] The package version must be `major.minor[.build]`** with decimal
    numbers only, and `name` must not end in "Plugin". SemVer pre-release
    suffixes such as `1.0.0-beta.1` are not allowed there.
    (Logi Actions SDK, `csharp/tutorial/plugin-structure.md`)
25. **[prototype] Generator defaults in `LoupedeckPackage.yaml`**:
    `author`/`copyright` = Windows user name, `supportedDevices` =
    `LoupedeckCtFamily`. Set them deliberately. The docs list no device id for
    the MX Creative Keypad; the only Keypad-specific name is the default
    profile `DefaultProfile70.lp5`.
    (Logi Actions SDK, `csharp/tutorial/plugin-structure.md`,
    `csharp/plugin-features/default-application-profiles.md`)
26. **[docs] The official samples reference `PluginApi.dll` from the installed
    service**, but since October 2026 the API is also on nuget.org as
    `LogiPluginApi` (6.4.2.3567, net10.0, requires Logi Plugin Service ≥ 6.4),
    and `logiplugintool generate` 6.4.2.3599 references it (as `6.*`). Use the
    package with `ExcludeAssets="runtime;native"` so that builds work without
    Options+ (e.g. in CI), and pin the version.
27. **[tests] `LogiPluginApi` brings `StyleCop.Analyzers` along**, despite
    `exclude="Build,Analyzers"` in its nuspec. With `TreatWarningsAsErrors`,
    Logitech's style rules (file headers, one type per file …) break the build.
    The generated project hides this with `NoWarn SA0001`; we remove that one
    analyzer in a target (`DropTransitiveStyleCop`).
28. **[tests] Also `CopyLocalLockFileAssemblies=true` from the template copies
    SkiaSharp, Newtonsoft.Json etc. into the plugin's `bin`**, although the
    service provides them. We set it to false; only our own assemblies are
    packed.
29. **[tests] `logiplugintool generate` writes an OS-specific
    `LoupedeckPackage.yaml`**: generated on Linux, `pluginFolderWin` is
    commented out. Keep the YAML in the repository and set it deliberately.
30. **[tests] The SDK's drawing code runs without the service.** With the
    `LogiPluginApi` runtime assembly and SkiaSharp's native library,
    `BitmapBuilder` renders keys to PNG on Linux too (`tools/KeyPreview`). There
    is no text measuring API, so text width is estimated (about 0.55 × font
    size per character, checked on rendered keys).
31. **[tests] Inside a `Plugin` subclass, `PluginStatus` means the inherited
    property, not the enum** of the same name. Use an alias
    (`using StatusKind = Loupedeck.PluginStatus;`).

32. **[docs+tests] Monochrome SVGs get recolored.** "Icon templates (.ict files)
    or other plugin color settings can overwrite the SVG image file colors …
    only in case the SVG image file is monochrome" (Logi Actions SDK,
    `csharp/icons/vector-images.md`). `BitmapImage.FromSvg` renders a
    single-color SVG white even without the service. Make icons two-tone (our
    badges: colored disc + dark glyph). Also, SVG colors must be set on the
    elements; a `stroke` on the root `<svg>` is ignored.

33. **[docs] Usage limits are only in the status line input.** Claude Code
    passes `rate_limits.five_hour` / `seven_day` (percent and `resets_at`, Pro
    and Max only, after the first response) to the status line command, not to
    hooks (code.claude.com/docs/en/statusline). Plugins cannot set the user's
    status line (`settings` in plugin.json only supports `agent` and
    `subagentStatusLine`), so it is a manual one-time setup.
34. **[docs] Whether Claude's permission dialog appears while a
    PermissionRequest hook is still running is not documented.** Similar
    keypad tools speak of a "fallback to the normal prompt" after their
    timeout, which suggests the dialog waits. Keep approval timeouts short and
    opt-in. Deny and ask rules are evaluated even when a hook returns "allow".
35. **[docs] Haptics need three things**: events in
    `events/DefaultEventSource.yaml` (or `PluginEvents.AddEvent`), waveforms in
    `events/extra/eventMapping.yaml`, and `HasHapticMapping` in
    `pluginCapabilities`; then `PluginEvents.RaiseEvent(name)` (Logi Actions
    SDK, `csharp/haptics/haptics-getting-started.md`; GitHub sample
    `HapticsPlugin`).
45. **[device] Options+ draws a white tab on folder keys.** Keys bound to a
    `PluginDynamicFolder` get a white bar over the plugin image, centered at
    the top edge, about 35 % of the key wide and the top ~15 % high (measured
    on an Options+ screenshot of a 60 px key). Commands
    (`PluginDynamicCommand`) don't get it, even with the same image. The Logi
    Actions SDK docs don't mention it
    (`csharp/plugin-features/implementing-dynamic-folders.md`). Seen in the
    Options+ device view; on the keypad's LCD it is about half as high. Keep the top
    center of folder images empty; our counter tiles put the badge top left.

## PowerShell / UI Automation

36. **[prototype] Claude keeps the stdout of a started process open.** Don't
    wait for stdout to end; the first result line counts (with a timeout).
37. **[prototype] PowerShell variables are case-insensitive**: `$sp` overwrites
    `$SP`. Spell out type names.
38. **[prototype] Dictation**: only invoke the Claude app's button, never
    Windows voice typing; never invoke "cancel dictation". If another chat is
    still visible, wait instead of dictating there. Group policies can block
    PowerShell scripts; log the error message.

## Build and setup

39. **[prototype] Updating by copying into a generated project leaves old
    source files behind.** A checked-in `.csproj` avoids this.
40. **[tests] `dotnet test` with xUnit v3 on the .NET 10 SDK** requires opting
    in to Microsoft.Testing.Platform in `global.json`
    (`"test": {"runner": "Microsoft.Testing.Platform"}`); the VSTest packages
    (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) are not needed.

## Claude Desktop on Windows

41. **[device] The Store (MSIX) version of Claude Desktop redirects AppData.**
    Files that Claude or anything it starts (hooks, Git Bash, the status line)
    *creates* below `%LOCALAPPDATA%` end up in
    `%LOCALAPPDATA%\Packages\Claude_<id>\LocalCache\Local\…`. Only processes
    inside the package see them at the normal path; Explorer and the Logi
    Plugin Service don't. Claude exempts its own folders (`Claude-Data`,
    `Claude-3p`, `Claude\logs`) through `ExcludedDirectories` in its
    `AppxManifest.xml`; we can't. The user profile itself is not redirected,
    hence the status directory `%USERPROFILE%\.sessionpulse`. Check with
    `Get-AppxPackage *Claude*`.
42. **[device] Checks from inside Claude prove nothing about such paths.** A
    shell started by Claude sees the merged view, so "the file is there" can be
    true for Claude and false for everyone else. Check in Explorer or a terminal
    started outside Claude.
43. **[device] Local plugins in the Code tab need no `claude` command.**
    `extraKnownMarketplaces` (source `directory`) plus `enabledPlugins` in
    `~/.claude/settings.json` load the plugin; `/reload-plugins` reports the
    hook count. They are listed under **+ → Plugins** next to the prompt box;
    Settings → Plugins only shows account (Cowork) plugins.
44. **[device] `bash` on the PATH can be WSL**, not Git Bash
    (`C:\Windows\System32\bash.exe`). Scripts and tests that need Git Bash on
    Windows must call it explicitly.
46. **[device] Cowork can run in the cloud, depending on version and account.**
    On one private setup it is a chat with a connected folder, and
    `local-agent-mode-sessions` holds only `remote-session-spaces.json`, no
    `local_*.json`. On another account that still offers Cowork as a separate
    session type, it runs locally and hooks work. For the cloud case:
    Claude Desktop 3.6556 logs the chat as a remote session (`cse_…`) and
    registers the computer as a "remote-tools-device" at
    `wss://bridge.claudeusercontent.com`. Only tool calls reach the computer:
    `main.log` shows `[remote-bash] vmUser=rcw-… mounts=<folder>:rw`, and
    `coworkd.log` shows one short-lived `bash` in the Linux VM per call, as
    user `rcw-<session id>` with the folder mounted at
    `/sessions/rcw-…/mnt/<folder>` (from `/mnt/.virtiofs-root/shared/c/…`). No
    Claude Code process and therefore no hook runs locally, so status files
    for these sessions are impossible. Chats without a folder don't touch the
    computer at all (see 6). Switching off "Run code in the cloud" (Settings →
    Capabilities) changes nothing: the next chat was again a `cse_…` session
    with one `remote-bash` call.
47. **[device] An uploaded (account) plugin also loads in local Code-tab
    sessions**, as `<name>@inline` (`main.log`: "Passing 2 plugin(s) to SDK
    (skills: 1, remote: 1 …)"; data dir `~/.claude/plugins/data/<name>-inline`).
    Together with the same plugin from a marketplace, every hook runs twice in
    parallel: the read-modify-write races, and with `APPROVALS=1` each
    permission request would be held twice. Install through one route only.
48. **[device] Claude Desktop's sidebar exposes session states to UI
    Automation**, e.g. a session button named "<title> Running". Electron only
    builds the accessibility tree after a first UIA query (14 elements, then
    ~500), it covers only what is rendered (the Code tab's sidebar, not other
    tabs), and names follow the app language. A possible, fragile source for
    sessions without hooks; not used yet.
49. **[device] The status line gave no usage data from the Code tab.** With
    `statusLine` in `~/.claude/settings.json` (Git Bash and the helper by
    absolute path; the helper itself works with sample input), no `usage.json`
    appeared after answers in Claude Desktop's Code tab. Not investigated
    further; the terminal `claude` was not tried. The usage key is hidden until
    there is a source that works there.
50. **[device] A simulated Alt press opens Claude Desktop's window menu.**
    Taking the focus from a background process with an Alt tap before
    `SetForegroundWindow` works, but when Claude is already in front (the
    deep link of the pressed key may also bring it there) the Alt press
    reaches Claude and opens its menu. Do nothing when the window in front
    belongs to a `claude` process; otherwise share the input of the window in
    front (`AttachThreadInput`) for the `SetForegroundWindow` call, and keep
    the Alt press only as a fallback.
51. **[device] CLI sessions keep their title in the transcript.** Claude Code
    (2.1.311) writes `{"type":"ai-title","aiTitle":"…"}` and, after `/rename`,
    `{"type":"custom-title","customTitle":"…"}` lines into the session's JSONL
    transcript (`transcript_path` in every hook payload). Terminal sessions
    have no Claude Desktop metadata, so this is their only title source.
    Transcripts grow to megabytes: read them once per turn (`Stop`) with one
    `grep`, never on the hot path.
52. **[device] Hooks can tell where Claude Code runs.** Claude Code sets
    `CLAUDE_CODE_ENTRYPOINT` (`claude-desktop` in the desktop app, `cli` in a
    terminal; the transcript has the same value as `entrypoint`) and
    `CLAUDE_PID` (its own Windows process id) for the hook.
53. **[device] A console handed over to Windows Terminal leaves no process
    link to it.** `claude.exe` started from `cmd.exe` in Windows Terminal (the
    default terminal app): parent `cmd.exe` (whose parent is `explorer.exe`),
    console host (`NtQueryInformationProcess`, `ProcessConsoleHostProcess`) a
    window-less `conhost.exe` stub, child of `cmd.exe`. And one
    `WindowsTerminal.exe` hosts all its windows, so its `MainWindowHandle`
    (the last active one) is a guess. Exact: `GetConsoleWindow()` after
    `AttachConsole(pid)` returns a `PseudoConsoleWindow` whose root owner
    (`GetAncestor(GA_ROOTOWNER)`) is the right Windows Terminal window; for a
    classic console it is the console window itself. `AttachConsole` affects
    the whole calling process, so it runs in a helper script
    (`show-terminal.ps1`, ~0.5 s), never in the Logi plugin host.
54. **[device] WSL user names can differ from Windows ones**, so
    `/mnt/c/Users/$USER` is no way to the Windows profile.
    `cmd.exe /c echo %USERPROFILE%` plus `wslpath -u` is (about 50 ms: once,
    then cached). Claude Code in WSL also has its own `~/.claude`, so plugins
    installed on Windows don't exist there.
55. **[device] Agent tools can change backslashes on the way.** The Bash tool
    swallowed doubled backslashes in commands, so fixtures written with
    `printf` from it contained `\c` where `\\c` was meant and grep tests
    seemed to fail; the editing tools decoded a backslash-u escape (for the
    middle dot) into the character itself. Write fixtures with a file editor,
    check ASCII-only files (Windows PowerShell 5.1 scripts) with `file`, and
    look at suspicious results byte by byte before blaming a tool.
56. **[device] Windows Terminal tabs carry the Claude Code session title**,
    with a status glyph in front ("✳ Casual test chat"; spinner frames while
    working), for WSL sessions too. In UI Automation a tab is a `TabItem`
    with `SelectionItemPattern`, so `Select()` switches tabs without the
    focus; terminal windows are top-level elements of class
    `CASCADIA_HOSTING_WINDOW_CLASS` (one `WindowsTerminal.exe` can host several
    windows, so `MainWindowHandle` is not enough). Matching the tab name to
    the hook's `title` finds the tab of any CLI session, also when no process
    id helps (WSL). `CLAUDE_CODE_DISABLE_TERMINAL_TITLE` turns the titles off.
57. **[device] Claude Code doesn't set the tab title in every terminal.** In
    WSL the Windows Terminal tab shows "✳ <title>"; the native Windows
    `claude.exe` (2.1.311) in a cmd console handed over to Windows Terminal
    left the tab at the profile name ("Eingabeaufforderung"), without
    `suppressApplicationTitle` or `CLAUDE_CODE_DISABLE_TERMINAL_TITLE`. So the
    window must be found without the title (53); the title only picks the tab
    where it is shown.
58. **[device] A Windows Terminal tab shows only the title of its active pane.**
    Two WSL sessions in split panes of one tab: one `TabItem` named after the
    focused pane, and the `TermControl` elements are named after the profile
    ("WSL", "Ubuntu"), so the other session's title appears nowhere and the
    title search (56) fails. `AttachConsole` on the session's `wsl.exe` does
    lead to the right window (53), but a WSL session doesn't know that Windows
    process id, and `wsl.exe` also runs for Docker and Claude Desktop (with
    hidden console windows). So the fallback is the first visible window with a
    WSL console; the pane can't be chosen.
59. **[device] `Select()` on the selected tab takes the keyboard focus away from
    the terminal.** The focus lands on the `TabItem`, so arrow keys and Enter
    don't reach a permission prompt. Switching to another tab is fine: Windows
    Terminal then focuses the terminal itself. Check `IsSelected` first.
60. **[device] "Its process is in front" doesn't mean "its window is in front"
    for Windows Terminal.** One process hosts all its windows, so with another
    terminal window in front the activator took the session's window for shown
    and did nothing. Compare window handles; the process check is only right
    for Claude Desktop (50), where a popup may be in front.
61. **[device] `Stop` doesn't mean the session is finished.** It fires when the
    main agent pauses, also with background agents running; they wake it later
    and it stops again. `background_tasks` in the payload tells the two apart,
    and it comes after `last_assistant_message`, so it can lie beyond the read
    limit. In `SubagentStop` the list still shows the stopping agent as
    `running`; the docs' example shows an empty list. (Seen with Claude Code
    2.1.316.)
62. **[device] Tool events of a subagent run through the same hooks as those of
    the main agent.** They carry `agent_id` (also `PermissionRequest`; seen with
    Claude Code 2.1.316), the main agent's don't. Without looking at it, a
    subagent's next tool call hides a question the main agent is waiting on.
