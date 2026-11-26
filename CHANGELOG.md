# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `KeyStats.Core`: incremental mirror backup of the database. Append-only
  tables (raw blocks, focus, titles, epochs) are copied from the last id, the
  others replaced whole, in one transaction over a consistent snapshot read
  without locking the source; the mirror is a single file. The nightly run is
  due once a night from 3:00 local time, or at the first chance after.
- `KeyStats.Agent`: single instance per database and Windows session (a
  second launch exits at once), and the control pipe: `snooze`, `pause`,
  `resume` and `quit` act like the tray menu, `flush` replies once the pending
  batch is written.
- `KeyStats.Core`: named-pipe server and client for the agent control channel.
  One client at a time and one command per connection; only the current user
  is admitted on either side, and the server refuses to start when the name is
  already taken. A client gone before the connection completes, a silent client
  or a slow handler leaves the server serving the next one.
- `KeyStats.Core`: line format of the agent control pipe, one ASCII command
  (`snooze`, `pause`, `resume`, `flush`, `quit`) and a one-line reply (`ok`,
  `unknown`, `failed`). The pipe is named per Windows session and per database.
- `KeyStats.Agent`: tray icon. A left click snoozes for 5 more minutes; the
  right-click menu snoozes until resumed, resumes now or quits. The icon turns
  grey with a pause badge, the tooltip shows the resume time and is refreshed
  every second only during a timed snooze. The icon comes back after an
  Explorer restart (`TaskbarCreated`).
- `KeyStats.Core`: `PauseLabel` gives the tray tooltip text: recording, snoozed
  until a local `HH:mm` with the minutes left, snoozed until resumed, or locked.
- `KeyStats.Agent`: the real agent. `KeyStats.Agent.exe [--db <path>]` runs
  capture, analysis and writing until the Windows session ends. A hidden window
  pauses on lock and sleep (epoch key dropped), resumes with a key-state reset
  and a clock resync, follows time zone changes and writes everything on
  `WM_ENDSESSION`. The capture clock is also resynced every 10 minutes.
- `KeyStats.Core`: `PauseSources` combines lock, sleep and the tray snooze
  (+5 minutes per click, or until resumed) into one external pause, posted as
  recorder commands. A snooze that expired unnoticed resumes at its planned end
  (never before the last posted change), and is settled before any later change.
  The epoch key is dropped on lock and sleep only.
- `KeyStats.Core`: lifecycle commands (pause, resume, epoch drop, time zone,
  flush) posted from any thread and applied by the ring pump in time order
  between keystrokes; a failing command is counted and skipped. `Posted` and
  `Applied` let a caller wait until its commands are applied (or failed), not
  just dequeued.
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
