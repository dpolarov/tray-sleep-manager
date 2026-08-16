# Changelog

All notable changes to SleepMngr are documented in this file.

## [1.0.1] - 2026-08-16

### Added

- Restored the tray `Автозапуск / Start with Windows` option for the current user via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Autostart writes are verified and failures remain non-fatal; actions are included in optional diagnostic logging.
- Added autostart state to the detailed Status window.

### Documentation

- Reworked the main `README.md` as the English documentation.
- Added a separate Russian `README.ru.md` with language links between both versions.
- Updated documentation for optional logging, `powercfg` diagnostics, Modern Standby, local builds, CI, releases, and autostart.
- Removed the redundant `README.en.md` after making English the default README.

## [1.0.0] - 2026-08-16

### Added

- Russian and English tray UI with automatic Windows UI-language detection and persistent language selection.
- GitHub Actions builds for compact and standalone Windows x64 packages.
- Automatic GitHub Release creation for tags matching `v*`.
- Optional tray logging switch (`Вести лог / Enable logging`), disabled by default and persisted in `%AppData%\SleepMngr\logging.txt`.
- Structured logging for mode changes, monitor/display events, lid and mouse-wake actions, `Sleep now`, auto-sleep, system session/power events, applied power state, and `powercfg` commands/results.
- Read-only/self-test mode used by CI to validate non-elevated `powercfg` failure handling without intentionally changing Windows power settings.

### Fixed

- Fixed `SetThreadExecutionState` thread-affinity handling so automatic sleep no longer releases execution state on a different thread.
- Prevented duplicate automatic sleep triggers and shutdown-time callbacks.
- Hardened timer, event, icon, menu, mutex, process, and WMI resource cleanup.
- Added WMI monitor-query caching with immediate invalidation on display changes.
- Hardened `powercfg` execution with exit-code, stderr, timeout, and process-start diagnostics while keeping failures non-fatal to the tray application.
- Improved mouse wake disable/restore handling with partial-change rollback.
- Reworked lid-action handling so original AC/DC values are never guessed and changes are verified after write.
- Replaced language-dependent lid-state parsing with native `PowrProf` reads (`PowerGetActiveScheme`, `PowerReadACValueIndex`, `PowerReadDCValueIndex`), with `powercfg` parsing kept only as a compatibility fallback.
- Fixed local builds being polluted by stale `SleepMngr.Tests/**` generated files; build scripts now target `SleepMngr.csproj` explicitly and cleanup removes nested test `bin/obj` folders.
- Logging disabled mode now suppresses both structured application logging and the legacy detailed sleep trace.

### Validated

- `Sleep now` works on a real Modern Standby laptop.
- Normal lid-close sleep works.
- `Always prevent sleep` keeps the laptop awake with the lid closed.
- External monitor connect/disconnect is detected and Auto mode switches correctly.
- Runtime language switching works.
- Logging toggle and non-elevated error handling pass CI self-tests.