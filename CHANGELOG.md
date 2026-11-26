# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `KeyStats.Agent`: the real agent. `KeyStats.Agent.exe [--db <path>]` runs
  capture, analysis and writing until the Windows session ends. A hidden window
  pauses on lock and sleep (epoch key dropped), resumes with a key-state reset
  and a clock resync, follows time zone changes and writes everything on
  `WM_ENDSESSION`. The capture clock is also resynced every 10 minutes.
- `KeyStats.Core`: lifecycle commands (pause, resume, epoch drop, time zone,
  flush) posted from any thread and applied by the ring pump in time order
  between keystrokes; a failing command is counted and skipped.
- `KeyStats.Agent`: analysis thread that drains the event ring into the
  recorder and writes to `%LOCALAPPDATA%\KeyStats\keystats.db`. The window-title
  key is protected by DPAPI. `--probe N --db <path>` runs the whole pipeline
  against a test database.
- `KeyStats.Core`: ring pump and writer between the capture and analysis
  threads. After a lost event, a key-state reset is written at the gap on the
  producer side. Window markers are resolved on the consumer side, and an
  exception is counted for the failing event only. `EventClock.PeekMs` gives
  the event time scale to another thread.
- `KeyStats.Core`: recorder for the analysis thread. It writes statistics, the
  encrypted raw block, focus rows with encrypted titles and unobserved periods
  in one transaction, every 60 s or on window change. Before encryption is set
  up, only statistics are written. External pauses (lock, sleep, snooze) stop
  all counting (key state resets still go through); an epoch drop or a time zone change writes pending data first.
  After the clock goes back, the 60 s write timer restarts from the new time.
- `KeyStats.Core`: key tracking (repeats, overlaps, chords, shortcuts), AZERTY
  layout with dead keys and Alt codes, writing-session detection and speeds,
  correction analysis (real typos versus collateral deletions), SQLite schema
  and stats writer, encrypted raw archive, Windows Hello and passphrase key
  wrapping, reference letter frequencies.
- `FocusState.Secret`: keystrokes in a detected password field only count
  keys, like an excluded app.
- `KeyStats.Agent`: Raw Input capture thread (keyboard and mouse buttons,
  listen-only, injected keystrokes dropped) feeding a lock-free event ring;
  `--probe <seconds>` prints capture counters only.
- Foreground window tracking: `FocusTracker`, `ExclusionList` (password
  managers, remote desktop, Windows credential prompts) and `FocusChannel`,
  which keeps each window change in keystroke order; the agent follows the
  active window and its title through `SetWinEventHook`, without polling, and
  never reads the title of an excluded app.
