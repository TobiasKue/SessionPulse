# Configuration

## Status directory

Set the environment variable `SESSIONPULSE_DIR` to move the status directory.
It must be visible to Claude (for the hook) **and** to the Logi Plugin Service
(for the plugin). Restart both after changing it. Defaults are listed in
[status-format.md](status-format.md#location).

In WSL the hook finds the Windows folder by itself (see the README). Set
`SESSIONPULSE_DIR` there only if that fails, as a `/mnt/…` path to the same
folder the plugin uses, e.g. in `~/.profile`:
`export SESSIONPULSE_DIR=/mnt/c/Users/<your Windows user>/.sessionpulse`.
The file `sessionpulse.conf` belongs into that Windows folder; the hook in WSL
reads it from there.

## sessionpulse.conf

An optional file `<status dir>/sessionpulse.conf`, read by the hook and by the
plugin. One `KEY=VALUE` per line, no spaces around `=`, `#` starts a comment
line. Unknown keys and invalid values are ignored, so a typo falls back to the
default instead of breaking anything.

| Key | Default | Used by | Meaning |
|---|---|---|---|
| `LABELS` | `1` | hook | `0`: don't derive key texts from the conversation (privacy): no Cowork label from the first prompt, no generated title for terminal sessions (a title set with `/rename` is still used). |
| `LABEL_LEN` | `28` | hook | Maximum label length in characters, 1–200. |
| `STALE_MINUTES` | `10` | plugin | A working session without activity for this long shows a clock ("stuck?"). |
| `CLEANUP_HOURS` | `24` | plugin | Status files not updated for this long are deleted. |
| `VERBOSE` | `0` | plugin | `1`: verbose diagnostics in the plugin log. |
| `DESKTOP_DATA_DIR` | `%LOCALAPPDATA%\Claude-Data` | plugin | Where Claude Desktop keeps its session metadata, if it differs. |
| `APPROVALS` | `0` | hook | `1`: answer permission requests on the keypad ([below](#approvals-on-the-keypad-optional)). |
| `APPROVAL_TIMEOUT` | `30` | hook | Seconds (5–280) to wait for the keypad before Claude asks as usual. |
| `HAPTICS` | `1` | plugin | `0`: no haptic events for the MX Master 4. |
| `STATUSLINE_CHAIN` | | status line helper | Your previous status line command; it gets the same input ([below](#usage-limits-hidden-for-now)). |

Example:

```
# Don't show prompt text on the keypad
LABELS=0
STALE_MINUTES=15
```

## Dictation (optional)

The **Dictation** key opens one chat in Claude Desktop and presses the app's
own dictation button; a second press stops dictation. It is off until you
create `<status dir>/dictation.json`:

```json
{
  "chatId": "the id from the chat's address, claude.ai/chat/<id>",
  "startButtons": "Dictate|Diktieren",
  "stopButtons": "Stop dictation|Diktat beenden"
}
```

* `startButtons` / `stopButtons`: the accessible names of Claude Desktop's
  buttons, several separated by `|` (for example per app language). The
  values above are examples; use the names your Claude Desktop shows (the
  button's tooltip, or an accessibility inspector).
* Safety rules: only buttons inside the Claude window are pressed (never
  Windows voice typing), a button named "cancel"/"abbrechen" is never pressed,
  and if a different chat is still shown, nothing is pressed.
* Windows only. If a group policy blocks PowerShell scripts, the plugin log
  shows the error.

## Approvals on the keypad (optional)

With `APPROVALS=1`, a permission request ("Allow Claude to run …?") is also
shown on the keypad: the **Approvals** key counts open requests and opens a
page with the request (tool and command; destructive commands in red with
"Risky!") and the answers **Allow**, **For session**, **Always** (both only when
Claude suggests a rule), **Deny** and **In Claude** (hand it back to Claude's
dialog). Pressing the request itself opens its session.

Things to know:

* While the hook waits, Claude may not show its own dialog yet. Keep
  `APPROVAL_TIMEOUT` short (default 30 s); after it, Claude asks as usual.
* "Always" saves Claude's suggested rule to the project's
  `.claude/settings.local.json`, exactly like choosing it in Claude.
* Deny and ask rules in your settings still apply; the keypad cannot allow
  what your settings forbid.
* Plan approval (`ExitPlanMode`) and questions (`AskUserQuestion`) are never
  held.
* Whether Cowork sends permission requests to hooks is not verified yet.

## Usage limits (hidden for now)

The keypad plugin currently has **no Usage limits key** and no usage haptic
event: on the first device test the status line delivered no data from Claude
Desktop's Code tab (docs/LESSONS.md). The status line helper and the plugin's
core still support it, so the key can come back. What follows describes the
helper.

The helper records the 5-hour and weekly usage of a Pro/Max subscription with
the time to the next reset in `usage.json`. Claude Code provides these
numbers only to its status line, so add the SessionPulse helper as status line
in `~/.claude/settings.json` (the hook keeps a copy at a stable path):

```json
{
  "statusLine": {
    "type": "command",
    "command": "bash ~/.sessionpulse/statusline.sh"
  }
}
```

On macOS use `bash "$HOME/Library/Application Support/SessionPulse/statusline.sh"`.
The helper prints a short line like `5h 23% · 7d 41%`. If you already have a
status line, keep it: put its command into `STATUSLINE_CHAIN` in
`sessionpulse.conf`, and the helper passes the same input on to it, e.g.
`STATUSLINE_CHAIN=npx -y ccstatusline@latest`.

The numbers update when a Claude Code session gets an answer. Cowork sessions
most likely do not run a status line (not verified), so keep one Claude Code
session around for current numbers.

## Haptics (MX Master 4)

The plugin raises three haptic events: **Session waiting for you**, **Approval
requested** and **Session finished**. In
Logi Options+ they appear under the SessionPulse plugin, where you can change
or turn off each one. `HAPTICS=0` turns them all off.
