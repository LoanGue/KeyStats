# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Security

- `KeyStats.Dashboard`: a renderer crash dump may hold the passphrase or
  decrypted text. WebView2 crash reports are no longer sent to Microsoft, the
  profile's Crashpad report folders deny file creation to the user, and
  anything found there is deleted at start, after a process failure and on
  close.

### Added

- `KeyStats.Core`: three new day metrics count capitals typed with the Shift of
  the opposite hand, with the Shift on the same side, or under Caps Lock, hands
  taken from the standard AZERTY fingering. Days recorded earlier have no Shift
  side until the recalculation from the raw data is available.
- `KeyStats.Core`: GET `/api/typos` gives the accuracy of each day of the
  period, the most confused pairs with an AZERTY neighbour flag, the error rate
  of accented characters, error types, the detection delay (median and
  distribution), the correction cost (collateral deletions, KSPC, keys lost)
  and the characters deleted by single, held and Ctrl+Backspace. Two new day
  metrics count backspace repeats and characters erased by Ctrl+Backspace;
  days recorded earlier need a recalculation from the raw data.
- `KeyStats.Dashboard`: Fautes page, first card: accuracy of each day of the
  period, with the period figure and the best day of at least 200 characters.
- `KeyStats.Dashboard`: Fautes page, confused pairs (filter on AZERTY
  neighbours) and the error rate of each accented character.
- `KeyStats.Dashboard`: Fautes page, share of each error type and distribution
  of the detection delay.
- `KeyStats.Dashboard`: Fautes page, cost of corrections (collateral deletions,
  KSPC, keys lost) and the share of characters deleted by single, held and
  Ctrl+Backspace.
- `KeyStats.Dashboard`: speed page from the design: each session of the period
  with the rolling median of the last 10, speed against accuracy for text
  sessions, and the distribution of speeds with the median and the peak.
- `KeyStats.Dashboard`: speed page, second half: median speed per application
  and per hour of the day, typing speed against real throughput week by week.
- `KeyStats.Core`: GET `/api/speed` gives each session of the period (start,
  application, real and typing speed, peak, accuracy, duration; the 5,000 most
  recent at most) and the typing speed and real throughput of the last 8
  calendar weeks.
- `KeyStats.Dashboard`: weekly progression chart on the overview page: speed,
  accuracy or writing time over the last 12 weeks, the current week dashed.
- `KeyStats.Dashboard`: overview page from the design: the period's keys,
  words, writing time, real speed and accuracy, each compared with the
  previous period.
- `KeyStats.Dashboard`: unlock dialog from the design (Windows Hello, waiting,
  failure, passphrase with show and hide), opened from a header button; a
  second header button locks the data again.
- `KeyStats.Dashboard`: shared view states from the design: loading, period
  without typing, agent stopped, read error with retry, and page not yet
  available. A slow view no longer overwrites the page opened after it.
- `KeyStats.Dashboard`: the page header shows whether the agent runs and whether
  the detailed data is unlocked, and carries the global filters (period,
  application, content, keyboard), kept in the address across pages.
- `KeyStats.Dashboard`: the page frame from the design: navigation between the
  dashboard pages, routed by the address fragment, and the page header.
- `KeyStats.Dashboard`: ECharts 6.1.0 (Apache-2.0) is vendored under
  `wwwroot/vendor/echarts/` for the charts, served locally like the rest of
  the page.
- `KeyStats.Core`: GET `/api/overview` gives the key figures of the period
  and of the previous one (keys, words, writing time, real speed, accuracy)
  and the last 12 calendar weeks of speed, accuracy and writing time.
- `KeyStats.Core`: the dashboard header filters (period, application, text
  kind, keyboard) are read from the request query by `StatsFilter`, with the
  previous period of the same length for comparisons. GET `/api/filters` lists
  the applications and keyboards to choose from and the first recorded day.
  Application, kind and keyboard only filter writing sessions; per-day tables
  ignore them.
- `KeyStats.Core`: `KeySetup` creates the master key on first run, writes its
  public part and its passphrase envelope (plus a Windows Hello one, through
  `IHelloEnroller`) in one transaction, and never replaces an existing key.
  POST `/api/setup` runs it, keeps the new key unlocked in the session and
  restarts the agent so it encrypts with the new public key.
- `KeyStats.Dashboard`: unlock the encrypted data with Windows Hello or the
  passphrase, and lock it again, from the page. The key is wiped when the
  window closes.
- `KeyStats.Dashboard`: POST requests from the page reach the router through
  a bridge; `RequestBody` in Core reads at most 16 KiB of body and clears its
  buffers.
- `KeyStats.Core`: `KeySession` keeps the master key in memory once unlocked by
  Windows Hello (through `IHelloSigner`) or the passphrase, and accepts only the
  key matching the stored public key. `/api/key` gives the state, POST
  `/api/key/unlock` and `/api/key/lock` change it. The router takes POST routes
  (body capped at 16 KiB, cleared once answered).
- `KeyStats.Dashboard`: WebView2 window that serves `https://keystats.app/`
  from memory through `WebResourceRequested`, with no server and no open port.
  Navigation to any other address and new windows are refused, SmartScreen
  URL checks are off, and responses carry a strict Content-Security-Policy and
  `Cache-Control: no-store`. `--db` picks the database.
- `KeyStats.Core`: `WebRouter` for the dashboard (API routes, then files of
  `wwwroot` under a closed list of types, path traversal refused, a fixed 500
  for any exception so no request is left to the network) and `/api/status`
  (encryption set up, agent running through `ControlProtocol.MutexName`).
- `KeyStats.Agent`: the live counter stays hidden while an application runs full
  screen (game, video) or in presentation mode (`SHQueryUserNotificationState`),
  and confirms each snooze click on the icon with the remaining time for 1.5 s.
- `KeyStats.Core`: `LiveDisplay.Snooze` (snooze confirmation, rounded up to the
  minute) and `LiveDisplay.Quiet` (notification states that hide the counter).
- `KeyStats.Agent`: application manifest declaring per-monitor DPI awareness
  (PerMonitorV2), so the live counter is drawn at each screen's scale, plus the
  usual `asInvoker` level and Windows 10 compatibility entries.
- `KeyStats.Agent`: live counter bottom right of the active window's screen,
  above the taskbar. Layered window drawn with per-pixel alpha, click-through,
  never activated, hidden from Alt+Tab and from screen capture. Its timer runs
  only while it is shown; a pause hides it, and `liveCounter.enabled` applies
  at start and on `reload`.
- `KeyStats.Core`: `LiveDisplay.Frame` gives the text of one counter frame (speed
  smoothed while typing, cpm on the same smoothing), and `LiveImage` composes
  the counter image (text on a dark rounded background, premultiplied alpha).
- `KeyStats.Core`: what the live counter shows and where. `LiveMeter.Shown`
  signals the start of real typing, so the display polls only while visible;
  `LiveDisplay` gives the text (wpm and cpm while typing, wpm and accuracy in
  the summary) and the bottom-right position above the taskbar, scaled to the
  screen DPI; `LiveSmoother` smooths the speed shown.
- `KeyStats.Core`: `settings.json` model (excluded apps, analysis thresholds,
  live counter, backup folder), read and written by source-generated JSON. A
  missing key keeps its default, an unknown key is ignored; a malformed, too
  large or out-of-bounds file gives the defaults and is left untouched. Saving
  goes through a temporary file. Format in `docs/settings.md`.
- `KeyStats.Agent`: loads `settings.json` at start, and rereads it on the new
  `reload` pipe command. Excluded apps and the backup folder apply at once,
  analysis thresholds at the next start; a refused file keeps the current
  settings and the command answers `failed`.
- `KeyStats.Agent`: nightly backup into `backup\` next to the database. Checked
  2 minutes after start, then every 10 minutes, on a pool thread with its own
  connections; capture and analysis go on meanwhile. A failure is retried an
  hour later, timed on a monotonic clock so that a clock set back does not
  extend the wait.
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

### Removed

- `KeyStats.Dashboard`: the unlock block from before the design, below the
  page; the header buttons and the unlock dialog replace it.
