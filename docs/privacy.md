# Privacy and transparency

SessionPulse works completely offline. Neither part opens a network connection.

## What the hook does

The Claude plugin `sessionpulse` registers one script,
`hooks/scripts/status.sh`, for these hook events: `SessionStart`,
`UserPromptSubmit`, `PreToolUse`, `PermissionRequest`, `PostToolUse`,
`PostToolUseFailure`, `Notification`, `SubagentStop`, `TaskCreated`,
`TaskCompleted`, `PreCompact`, `PostCompact`, `Stop`, `StopFailure`,
`SessionEnd`.

For each event it reads the event JSON and keeps **one small file per
session** in the status directory ([format](status-format.md)). The file
contains:

* the session id, the state and a detail code,
* timestamps and counters (prompts, tool calls, tasks),
* the name of the current tool (e.g. `Bash`), without its arguments,
* a short label: the folder name for Claude Code sessions, the first
  ~28 characters of the first prompt for Cowork sessions (turn off with
  `LABELS=0`), or the name of a scheduled task,
* the session title (at most 80 characters): the one you set with `/rename`,
  or the one Claude Code generated (turn off with `LABELS=0`). To find it, the
  hook searches the session's transcript once per answer for these two title
  lines and keeps nothing else from it,
* where the session runs: Claude Code's entry point (`cli`, `claude-desktop`),
  its process id (to bring the right terminal window to the front) and, in
  WSL, the name of the distribution.

It does **not** store tool inputs or outputs, file contents, answers or any
other conversation content.

In WSL the hook additionally keeps the path of your Windows user folder in
`~/.cache/sessionpulse/windows-dir`, so it can write where the keypad plugin
looks. It prints nothing, so nothing is added to Claude's
context, and by default it never blocks or changes an action.

With `APPROVALS=1` (off by default) the hook also writes the open permission
request to `approvals/`: tool name and a preview of up to 160 characters (the
command, file name, URL or search query). The file exists only while the
request is open and is deleted when it is answered or times out. Only in this
mode does the hook wait (up to `APPROVAL_TIMEOUT`) and print the keypad's
decision for Claude.

The optional status line helper writes `usage.json`: the usage percentages and
reset times of your subscription windows, nothing else.

## What the keypad plugin reads

* The status files above.
* Claude Desktop's session metadata
  (`%LOCALAPPDATA%\Claude-Data\…\local-agent-mode-sessions\…` and
  `…\claude-code-sessions\…`) to show the session title and open the right
  session. These files also contain the system prompt, your e-mail address and
  the first message. The plugin reads at most the first 64 KB of each file,
  stops at the `systemPrompt` field, and keeps only `sessionId`,
  `cliSessionId`, `title`, `isArchived`, `createdAt` and `scheduledTaskId`.
* When you press the key of a terminal session: process names, parent
  processes and console hosts, to find the terminal window that shows it. It
  reads nothing from the terminal.

## What the keypad plugin writes

Only its own state, in the plugin data directory of the Logi Plugin Service:
acknowledged sessions (`acked.json`: session ids and counters) and the read
state of scheduled runs (`scheduled-state.json`: session ids and the day of the
first start). Status files older than `CLEANUP_HOURS` are deleted.

## What the helper scripts look at

Two optional PowerShell scripts (Windows UI Automation) run only when you press
a scheduled run or the dictation key. They look only into the Claude Desktop
window: the address of the shown conversation (to check it is the right one),
scrollable areas (to scroll to the top) and button names (to find the
dictation button). They read no message text and print a one-line result.

A third one runs when you press the key of a terminal session: it attaches to
the session's console only to ask which window shows it, and reads the tab
names of Windows Terminal windows to select the session's tab. It reads
nothing from the console or inside the tabs and prints only the handle of the
window it used.

## Logs

The plugin logs counts and states (and with `VERBOSE=1` also session ids),
never labels or titles.
