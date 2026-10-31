# Status file format (v2)

The Claude hook and the keypad plugin talk only through files. This page is the
contract between them. Writer: `claude-plugin/hooks/scripts/status.sh`.
Reference reader: `src/SessionPulse.Core/Status/StatusFileParser.cs`.

## Location

```
<status dir>/
├─ sessionpulse.conf          optional, see configuration.md
├─ statusline.sh              copy of the status line helper (written on SessionStart)
├─ usage.json                 subscription usage, written by the status line helper
├─ sessions/
│  ├─ <session_id>.json       one file per session
│  └─ .<session_id>.<pid>.tmp transient, while a write is in progress
└─ approvals/                 only with APPROVALS=1
   ├─ <request id>.json       a permission request held for the keypad
   └─ <request id>.decision   the keypad's answer
```

The status directory is `$SESSIONPULSE_DIR` if set, otherwise:

| Platform | Default |
|---|---|
| Windows | `%USERPROFILE%\.sessionpulse` (not below AppData, see LESSONS: Store version of Claude Desktop) |
| WSL (hook only) | the Windows default, as `/mnt/<drive>/…/.sessionpulse` (see below) |
| macOS | `~/Library/Application Support/SessionPulse` |
| Linux (tests only) | `${XDG_STATE_HOME:-~/.local/state}/sessionpulse` |

Both sides compute the same default. `StatusPathsTests.HookWritesToTheSameDefaultDirectory`
checks this by running the real hook; `tests/hook/run.sh` checks the Windows
and WSL defaults.

In WSL (`WSL_DISTRO_NAME` set) the hook writes to the Windows profile, because
the keypad plugin runs on Windows. It asks Windows once
(`cmd.exe /c echo %USERPROFILE%`, converted with `wslpath -u`), caches the
result in `${XDG_CACHE_HOME:-~/.cache}/sessionpulse/windows-dir` and falls
back to the Linux default if that fails.

`<session_id>` is the hook's `session_id` with every character outside
`[A-Za-z0-9_-]` removed, cut to 128 characters. Payloads without a usable id
are ignored.

## Writing

* One line of UTF-8 JSON followed by `\n`, no BOM.
* Atomic: the hook writes `.<id>.<pid>.tmp` and renames it over `<id>.json`.
  Readers only look at `*.json` and must open files with
  `FileShare.ReadWrite | FileShare.Delete` (Windows), otherwise the rename fails.
* Field order is fixed (it keeps the hook tests simple), but readers must not
  depend on it.

## Fields

```json
{"v":2,"id":"…","src":"code","label":"SessionPulse","sched":"Daily digest","title":"Fix the login",
 "state":"working","detail":"tool","tool":"Bash","event":"PreToolUse",
 "started":1761230000,"turnStarted":1761230100,"activity":1761230105,"updated":1761230105,
 "prompts":3,"tools":17,"tasksDone":2,"tasksTotal":5,"host":"cli","pid":22616}
```

| Field | Type | Meaning |
|---|---|---|
| `v` | int | Format version, `2`. |
| `id` | string | Sanitized session id (= file name). Matches `cliSessionId` in the Claude Desktop metadata, not its `sessionId`. |
| `src` | `code` \| `cowork` | `cowork` if the session's first `cwd` contains `local-agent-mode-sessions`. Fixed after the first event. |
| `label` | string | Short display text, may be empty. See [Labels](#labels). |
| `sched` | string, optional | Name of the scheduled task, taken from a first prompt that starts with `<scheduled-task name='…'`. Omitted for normal sessions. |
| `state` | enum | `idle`, `working`, `waiting`, `done`, `ended`. |
| `detail` | enum | Why the session is in this state, see [Detail codes](#detail-codes). Not localized; readers translate. |
| `tool` | string, optional | Tool name while `detail` is `tool` or `permission`. MCP tools are shortened to the part after the last `__`, at most 24 characters. |
| `event` | string | Last hook event that touched the file. |
| `started` | unix s | First event seen for this session. |
| `turnStarted` | unix s | Last `UserPromptSubmit`; 0 if none yet. |
| `activity` | unix s | Last *real* activity (see table below); 0 if none. Use this, not `updated`, to judge whether a session is alive or stuck. |
| `updated` | unix s | Last write of any kind. Use it for cleanup only: Cowork sends `SessionStart`/`SessionEnd` when a session is merely opened or switched to. |
| `prompts` | int | Number of `UserPromptSubmit` events. |
| `tools` | int | Number of `PreToolUse` events. Best effort: parallel tool calls run parallel hooks, which can lose an increment. It still only goes up, which is all acknowledging needs. |
| `tasksDone`, `tasksTotal` | int | Task progress, from `TodoWrite` (absolute) or `TaskCreated`/`TaskCompleted` (counted). `tasksDone ≤ tasksTotal`. |
| `agents` | int, optional | Background agents (`subagent` and `workflow` entries of `background_tasks`) still running at the last `Stop` or `SubagentStop`. Omitted when 0. Other task types don't count: shells and monitors may run as long as the session, and teammates stay in flight while idle. |
| `waitAgent` | string, optional | `agent_id` of the subagent a `waiting` session waits for. Omitted when the main agent waits or the session isn't waiting. Used by the hook itself. |
| `title` | string, optional | Session title from the transcript (`transcript_path`), read on `Stop`: the last `customTitle` (`/rename`), else the last generated `aiTitle` (only with `LABELS=1`). At most 80 characters. Kept until a newer one is found. |
| `host` | string, optional | `CLAUDE_CODE_ENTRYPOINT` of the Claude Code running the hook: `claude-desktop` for the desktop app, `cli` in a terminal, other values for SDK hosts. |
| `pid` | int, optional | `CLAUDE_PID`, the process id of that Claude Code. Omitted in WSL, where it would be a Linux process id. |
| `wsl` | string, optional | `WSL_DISTRO_NAME` when the session runs in WSL. |

Numbers are non-negative integers. Strings use standard JSON escaping; control
characters are replaced by spaces. Labels are cut by characters, but readers
should still decode invalid UTF-8 leniently.

## Events

| Hook event | `state` → | `detail` → | Activity | Other |
|---|---|---|---|---|
| `SessionStart` | `idle` for a new file, otherwise unchanged | | no | Cowork also sends it on open/switch, so it never resets a state. |
| `UserPromptSubmit` | `working` | `thinking` | yes | `turnStarted = now`, `prompts++`, label/`sched` on the first prompt |
| `PreToolUse` | `working` | `tool` | yes | `tools++`; `AskUserQuestion` → `waiting`/`question`, `ExitPlanMode` → `waiting`/`plan`; `TodoWrite` sets task progress |
| `PermissionRequest` | `waiting` | `permission` | yes | fires immediately, unlike the notification |
| `PostToolUse` | `working` | `thinking` | yes | |
| `PostToolUseFailure` | `working` (`idle` if `is_interrupt`) | `thinking` (`interrupted`) | yes | |
| `Notification` `permission_prompt` | `waiting` | `permission` | yes | |
| `Notification` `elicitation_dialog`, `elicitation_url_dialog`, `agent_needs_input` | `waiting` | `input` | yes | |
| `Notification` `idle_prompt` | `done` if `working` and `agents` is 0, else unchanged | | no | |
| other `Notification` types | unchanged | | no | |
| `SubagentStop` | unchanged | `subagent` if `working` | yes | `agents` from `background_tasks` |
| `TaskCreated` / `TaskCompleted` | unchanged | | yes | `tasksTotal++` / `tasksDone++` |
| `Stop` | `done`; `working` while background agents run | `done`; `subagent` while background agents run | yes | `agents` from `background_tasks`; `title` from the transcript (one `grep`) |
| `StopFailure` | `done` | `limit` for `error: rate_limit`, else `error` | yes | API error ended the turn; `agents = 0` |
| `PreCompact` | `working` | `compacting` | yes | |
| `PostCompact` | `idle` after a manual `/compact`, else `working` | `idle` / `thinking` | yes | |
| `SessionEnd` | `ended` | `ended` | no | `agents = 0`. Cowork sends it after every answer: readers treat `ended` like `done`. |

A `waiting` session stays `waiting` until the agent it waits for moves on:
`PreToolUse`, `PermissionRequest`, `PostToolUse` and `PostToolUseFailure` of any
other agent change nothing except `tools` and `event`. No event counts as
activity while the session stays `waiting`, so `activity` tells how long it has
been waiting. The wait ends with an event of that agent, its `SubagentStop`, a
`UserPromptSubmit`, an interrupt or, unless a running subagent is what it waits
for, `Stop`.

## Detail codes

`idle`, `thinking`, `tool`, `question`, `plan`, `permission`, `input`,
`subagent`, `interrupted`, `done`, `error`, `ended`, `compacting`, `limit`.

New codes may be added without a version bump. Readers map unknown codes to a
generic text.

## Labels

Priority, highest first:

1. A custom session title (`session_title` in the payload, set e.g. with `/rename`).
2. The scheduled task name (`sched`).
3. Code: the name of the folder the session started in.
4. Cowork: the first prompt, whitespace collapsed, cut to `LABEL_LEN`
   characters. Switch off with `LABELS=0`.

For the key text the plugin prefers the title from the Claude Desktop metadata,
then `title`, then `label`.

## Approvals (opt-in)

With `APPROVALS=1`, the hook holds a `PermissionRequest` (except for
`AskUserQuestion` and `ExitPlanMode`) for up to `APPROVAL_TIMEOUT` seconds:

1. It writes `approvals/<id>.json` (atomically):

   ```json
   {"v":1,"id":"<id>","session":"<session_id>","tool":"Bash","preview":"rm -rf build",
    "risk":"high","remember":true,"created":1761230000,"expires":1761230030}
   ```

   `preview` is the command, file name, URL or query (at most 160 characters);
   `risk` is `high` for destructive shell commands, `edit` for file edits,
   otherwise `normal`; `remember` says whether Claude suggested a permission
   rule (needed for "for session" / "always"). `<id>` contains a random part.
2. The keypad plugin answers with `approvals/<id>.decision`, one word:
   `allow`, `session`, `always`, `deny` or `ask`. It only answers ids whose
   request file exists.
3. The hook polls for the decision every 0.25 s, deletes both files and prints
   the matching `PermissionRequest` decision on stdout (`session` and `always`
   echo Claude's first `permission_suggestions` entry, with destination
   `session` for "for session"). `ask` or no answer in time: no output, so
   Claude shows its own dialog.

## usage.json

Written by `claude-plugin/statusline/statusline.sh` from the status line data
of Claude Code (`rate_limits`, Pro/Max subscriptions only):

```json
{"v":1,"updated":1761230000,"fiveHour":{"pct":23.5,"resetsAt":1761240000},
 "sevenDay":{"pct":41,"resetsAt":1761800000}}
```

Windows that Claude Code does not report are omitted (`spend` appears behind a
Claude apps gateway). Readers treat a window whose `resetsAt` has passed as 0 %.

## Compatibility rules

* Readers ignore files whose `v` is not `2` (log it once per file).
* Readers ignore unknown fields and use defaults for missing optional fields
  (`""`, `0`, absent).
* Adding an optional field or a detail code keeps `v` at 2.
* Removing or renaming a field, changing a type or adding a `state` value
  requires `v: 3`.
