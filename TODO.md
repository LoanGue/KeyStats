# TODO

Build plan for KeyStats. One issue and one PR per step.

- [x] 1. Solution skeleton: three projects, tests, `CLAUDE.md`
- [x] 2. Capture prototype: Raw Input, NativeAOT with SQLite, bytes per raw event (throwaway, not kept)
- [x] 3. Core: analysis, SQLite storage, encrypted raw archive, unit tests (#2)
- [x] 4. Agent: Raw Input capture, key-state resets, foreground window and title, batched writes (#6)
  - [x] Notification icon with stackable 5-minute snooze (#41)
  - [x] Single instance, named pipe, settings, nightly backup (#54)
- [x] 5. Live wpm/cpm counter, bottom right (#55)
- [ ] 6. Dashboard: WebView2 host and JSON API, Windows Hello and setup wizard
  - [ ] Pages and charts, AZERTY heatmap
  - [ ] Millisecond view and history recalculation
- [ ] 7. `install.ps1`, `uninstall.ps1`, README
- [ ] Repository standards: LICENSE, SECURITY.md, CONTRIBUTING.md
