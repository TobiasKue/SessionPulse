# Device test plan

First test of SessionPulse on real hardware (Windows, Claude Desktop, Logi
Options+, MX Creative Keypad; macOS in section 8). Everything below was built and unit-tested
without a device; this list covers what could not be tested that way.

Tick the boxes, and write anything unexpected into **Findings** at the end
(what you did, what you saw, log lines). Logs:

* Keypad plugin: `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\plugin_logs\`
  (set `VERBOSE=1` in `%USERPROFILE%\.sessionpulse\sessionpulse.conf` for more)
* Status files: `%USERPROFILE%\.sessionpulse\sessions\`

## 0. Preparation

- [ ] Logi Options+ is up to date; Logi Plugin Service version is **6.4 or later**
      (Options+ settings, or the log). Note the version: ______
- [ ] The prototype is uninstalled (old plugin, old Claude hook, old `.link`
      files in `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\`).
- [ ] Git for Windows is installed (Claude runs hooks in Git Bash).
- [ ] .NET 10 SDK installed (only needed to build from source).

## 1. Claude plugin (hook)

- [ ] Install for Claude Code: `claude plugin marketplace add <repo path>`,
      then `claude plugin install sessionpulse@sessionpulse`.
- [ ] Install in Cowork (as with the prototype). Write down the exact steps
      for the README: ______
- [ ] Send a prompt in Claude Code: a file appears in `sessions\`, state
      `working`, then `done`.
- [ ] Same in Cowork: `src` is `cowork`, the label is the start of the prompt.
- [ ] A tool call that needs approval: state `waiting`, detail `permission`
      (immediately, not only after ~6 s).
- [ ] Claude asks a question (AskUserQuestion): `waiting` / `question`.
- [ ] Press Esc during a tool call: `idle` / `interrupted`.
- [ ] Nothing from the hook shows up in the conversation, and there are no
      "hook error" notices.
- [ ] Hook speed: tool calls do not feel slower than without the plugin.
      (The CI log of the Windows job prints the average time per hook run.)
- [ ] A scheduled task run: the status file has `sched` with the task name.
- [ ] Optional: send me (anonymized) payloads of Cowork events so they can
      become `recorded-*` test cases.

## 2. Keypad plugin: installation

- [ ] Install `SessionPulse_*.lplug4` (double-click; the installer dialog may
      be hidden behind other windows).
- [ ] Log shows `SessionPulse loaded: status dir ..., Claude Desktop data ...`.
      Is the Claude Desktop data path right? ______
- [ ] The plugin appears for the **MX Creative Keypad** in Options+ (group
      "SessionPulse"). If not: the `supportedDevices` value is wrong. Look at
      `metadata\LoupedeckPackage.yaml` of another plugin that works on the
      keypad and note its value: ______
- [ ] Before the first prompt, Options+ shows the plugin warning
      "No session status yet"; it disappears after a prompt.

## 3. Session keys

- [ ] Assign Session 1–8. Empty keys show only a dim number.
- [ ] **The image fills the whole key** (no shrunken image with a name below).
      If not, `DefaultIconTemplate.ict` does not work as expected.
- [ ] Working session: blue badge with spinning ring, time counts up.
- [ ] Waiting session: key pulses yellow, bell badge, yellow pill with the reason.
- [ ] Done session: green check, green tint.
- [ ] Title comes from Claude Desktop (not the folder name / prompt start).
- [ ] Text is readable and no word is cut in the middle (long titles end with "…").
- [ ] Press a key: **the right session opens** and Claude comes to the front,
      also when Claude was minimized or behind other windows.
- [ ] Press a done session: it opens and the key becomes free (acknowledged).
      After the next prompt in that session it shows up again.
- [ ] A session keeps its key while others start and finish.
- [ ] More than 8 sessions: a waiting session replaces the oldest done one.
- [ ] A working session without events for 10 min shows the orange clock.
- [ ] Restart the Logi Plugin Service: acknowledgements are kept.

## 4. Counter folders

- [ ] Assign Working / Waiting / Done sessions. Tile shows badge and count;
      gray at 0; the count updates live.
- [ ] Open a folder: back key plus the matching sessions.
- [ ] Press a session in a folder: it opens, the folder closes, and
      **no other key is triggered on the main page (ghost press)**.
- [ ] Done folder: last key "Alle quittieren" clears all done sessions.

## 5. Scheduled tasks

- [ ] Assign "Scheduled tasks". After a scheduled run: count 1, the folder key
      is yellow and pulses, yellow dot on the run's key, unread runs first.
- [ ] Title is the run's title (or an earlier run's title while the new run is
      still unnamed), never `<scheduled-task ...`.
- [ ] Press the run: it opens, **scrolls to the top**, the dot disappears.
- [ ] Runs from before the first start count as read.
- [ ] Archived runs do not appear.

## 6. Dictation (optional)

- [ ] Without `dictation.json` the key shows "Nicht eingerichtet".
- [ ] Create `dictation.json` (docs/configuration.md) with the chat id and the
      real button names of Claude Desktop: start ______ / stop ______
- [ ] Press: the chat opens, dictation starts, the key pulses red.
- [ ] Press again: dictation stops.
- [ ] With another chat in front, nothing is dictated into it.
- [ ] If PowerShell scripts are blocked by policy, the log shows the reason.

## 7. New since the first plan

- [ ] **Next waiting session** key: shows the number waiting; opens the
      longest-waiting session; with none waiting it opens the newest done one.
- [ ] Compacting (`/compact`): violet badge and "Verdichtet" pill.
- [ ] A usage limit or API error ends a turn: red badge with "Limit"/"Fehler".
- [ ] **Approvals** (`APPROVALS=1` in sessionpulse.conf):
  - [ ] A permission request appears on the Approvals key (count, pulsing).
  - [ ] Does Claude show its own dialog while the keypad waits, or only after
        the timeout? Note: ______
  - [ ] Allow, For session, Always, Deny, In Claude each do what they say;
        "Always" adds the rule to `.claude/settings.local.json`.
  - [ ] `rm -rf …` shows red with "Riskant!".
  - [ ] Without an answer, Claude asks as usual after `APPROVAL_TIMEOUT`.
  - [ ] In Cowork: are permission requests held at all? Note: ______
- [ ] **Haptics** on the MX Master 4: waiting, approval, done.
      The events are listed for the plugin in Options+ and can be turned off.

## 7a. Terminal and WSL sessions

- [ ] `claude` in Windows Terminal (cmd or PowerShell): the key shows the
      generated title after the first answer, not the folder name; after
      `/rename` the new title.
- [ ] Pressing that key brings the Windows Terminal window to the front, also
      when it was minimized; Claude Desktop does not open.
- [ ] With several tabs (another tab in front): the session's tab gets
      selected, shortly after the window comes up (only where the tab shows
      the session title; note for cmd / PowerShell: ______).
- [ ] Two Windows Terminal windows, session in the one behind: that window
      comes to the front (about half a second after the press), not the other;
      also when the other terminal window has the focus.
- [ ] Same in a classic console window (`conhost`) and in the VS Code
      terminal, if used: the right window comes to the front.
- [ ] Terminal closed, key pressed: nothing opens, the log says
      "no terminal window found" / "terminal tab not selected".
- [ ] WSL: install the plugin inside WSL as in the README; after a prompt a
      file appears in `%USERPROFILE%\.sessionpulse\sessions\` with `"wsl":"…"`;
      `~/.cache/sessionpulse/windows-dir` names that folder.
- [ ] WSL: the key shows the title; pressing it selects its tab and brings
      Windows Terminal to the front.
- [ ] A session waiting for a permission: after the press, arrow keys and Enter
      answer the prompt right away (the focus is in the terminal, not on the
      tab), with its tab already selected and with another tab in front.
- [ ] WSL, two sessions in split panes of one tab: pressing the key of the
      pane without focus, or of a session without a title yet, brings the
      window to the front (the pane stays as it is).
- [ ] Hook speed in WSL (writing to `/mnt/c`): tool calls don't feel slower.

## 8. macOS

Sections 1–5 apply on the Mac too, with these paths: logs in
`~/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/`,
status files in `~/Library/Application Support/SessionPulse/sessions/`.
Dictation and scrolling to the top are Windows only.

- [x] Hook installed with `claude plugin install`; a fresh session writes
      `host` and `pid` (bash 3.2 at `/bin/bash`).
- [x] `.lplug4` installs; log shows `SessionPulse loaded` with the macOS paths.
- [x] Terminal session: the key brings Terminal to the front and selects the
      right tab.
- [x] Code tab of Claude Desktop: the key opens the right session.
- [ ] iTerm2 session: the right tab is selected (asks for Automation once).
- [ ] VS Code or another terminal: the app comes to the front.
- [ ] Claude Desktop started from the Dock (no Homebrew in PATH): status files
      are still written.
- [ ] Cowork session (where it runs locally) appears.
- [ ] Waiting session after `/rename`: the key shows the new title after the
      next turn and still opens the session.

## Findings

<!-- One entry per problem: section/step, what happened, log lines. -->
