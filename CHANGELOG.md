# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-14

### Added

- Claude plugin `sessionpulse` with a hook that writes one status file per
  session (status format v2, `docs/status-format.md`).
- Golden tests for the hook, including robustness and timing checks.
- `SessionPulse.Core`: status file parser, status directory and configuration
  handling, shared by the upcoming keypad plugin.
- Documentation: architecture, status format, configuration, privacy, lessons
  learned.
- Keypad plugin `SessionPulse` (not yet tested on a device): eight session
  keys with state, running time, source, title and task progress; pressing a
  key opens the session in Claude Desktop and acknowledges it when finished;
  plugin status warning while no status file exists; diagnostics in the plugin
  log; English and German texts.
- `SessionPulse.Core`: session store with file watcher and polling,
  acknowledgements (`acked.json`), stable key assignment, Claude Desktop
  metadata index (privacy-preserving reader) and deep links, `Dashboard`.
- `tools/KeyPreview`: renders all key states to PNG; CI packs and verifies the
  `.lplug4`.
- Counter folders "working", "waiting", "done" with "clear all", protected
  against ghost presses.
- Scheduled tasks folder: newest run per task, unread first, own read state,
  scrolls the run to the top when opened.
- Optional dictation key (Windows), configured with `dictation.json`.
- Key design: two-tone SVG state badges, reason pill for sessions that need
  you, rounded progress bar, unread marker.
- "Next waiting session" key.
- Optional keypad approvals for permission requests (allow once, for the
  session, always, deny, hand back to Claude), with a risk marker for
  destructive commands; file handshake with the hook, opt-in via `APPROVALS=1`.
- Optional status line helper that records the 5-hour and weekly usage in
  `usage.json` and can chain an existing status line. The keypad key for it
  and its haptic event (usage at 90 %) are hidden for now: the status line
  gave no data from Claude Desktop's Code tab.
- New states: compacting, and finished by a usage limit or an error.
- Haptic events for the MX Master 4 (session waiting, approval requested,
  session finished).
- Release workflow: tag `vX.Y.Z` publishes the `.lplug4` and the Claude plugin
  zip; CONTRIBUTING, issue templates.
- Claude Code in WSL: the hook writes to the Windows status directory (asks
  Windows once for the user folder and caches it), so WSL sessions appear on
  the keypad. README explains the installation per Claude installation.
- Terminal sessions (`claude` on Windows or in WSL) show the title Claude Code
  generated or the one set with `/rename`, read from the transcript once per
  answer, instead of the folder name.
- Pressing the key of a terminal session brings its terminal window to the
  front (the exact one, also with several Windows Terminal windows) and
  selects the session's Windows Terminal tab where Claude Code shows the
  session title, e.g. in WSL, instead of opening Claude Desktop.
- Status file: optional fields `title`, `host`, `pid` and `wsl`.
- macOS: the keypad plugin installs and runs on the Mac. A session key opens
  the Claude Desktop session or brings the terminal app to the front, and
  selects the tab in Terminal and iTerm2. The hook works with the bash 3.2
  that macOS ships. Dictation and scrolling to the top stay Windows only.

### Changed

- License: MIT with Commons Clause instead of plain MIT. Free to use, change and
  share, also commercially, but not to sell. Both packages include the license.
- Windows status directory is now `%USERPROFILE%\.sessionpulse` instead of
  `%LOCALAPPDATA%\SessionPulse`: the Store version of Claude Desktop redirects
  files its hooks create below AppData, so the keypad plugin never saw them.
- Counter tiles (folders and "next waiting") show their badge top left and a
  larger number, so the white tab Options+ draws on folder keys no longer
  covers the badge.

- Unread scheduled runs ask for attention: the folder key is yellow and pulses
  like a waiting session, and the marker on the run's key is yellow, not blue.

### Fixed

- A session whose main agent has finished stays "working" while background
  agents still run, instead of showing as done (`background_tasks` in the `Stop`
  payload, new optional status field `agents`).
- A waiting session no longer switches to "working" because another agent keeps
  working; only the agent it waits for ends the wait (new optional status field
  `waitAgent`).

- Pressing a key while Claude Desktop is already in front no longer opens
  Claude's window menu: bringing Claude to the front uses no simulated Alt
  press any more (only as a fallback when another window is in front).
- Pressing the key of a WSL session now also brings its terminal window to the
  front when its title is on no tab: in a split pane without focus, or before
  the first answer.
- Pressing the key of a terminal session whose tab is already selected leaves
  the keyboard focus in the terminal, so a prompt can be answered right away.
- Pressing the key of a terminal session now also switches to its window when
  another Windows Terminal window is in front.
