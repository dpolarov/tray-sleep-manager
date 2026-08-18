# Changelog

All notable changes to SleepMngr are documented in this file.

## [1.0.2] - 2026-08-18

### Fixed

- Fixed a Modern Standby hang where broadcasting the display-off command with synchronous `SendMessage(HWND_BROADCAST, ...)` could block the tray UI for several minutes if another top-level window did not respond. The display-off request now uses non-blocking `PostMessage` and reports Win32 errors when posting fails.
- Fixed loss of the user's original lid-close AC/DC settings across exit, forced process replacement, crash, or restart. Original lid settings are now persisted to `%AppData%\SleepMngr\lid_original.txt` before the app changes them, recovered by the next instance when needed, and removed only after a verified restore.
- Normal shutdown now retries restoration of the original lid settings through application/process exit hooks.

### Validated

- On a real Modern Standby laptop, external-monitor disconnect with the lid closed triggers AutoSleep without freezing SleepMngr.
- The display-off request returns immediately and Windows locks the session immediately afterward.
- Original lid settings were repeatedly saved as `AC=1, DC=1`, restored to `AC=1, DC=1` on disconnect, and correctly saved again after reconnect.
- Repeated external-monitor connect/disconnect cycles remained consistent after the recovery changes.

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