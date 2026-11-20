# Changelog

All notable changes to **SysTelemetry** will be documented in this file.

## [1.0.0] - 2026-11-20

### Added
- **Three-Layer Telemetry Architecture**:
  - Clickable taskbar bar (`TaskbarBarWindow`) supporting `Left` / `Right` docking (with Windows 11 Widgets `TaskbarDa` avoidance) and horizontal fine-tuning.
  - Expandable telemetry popover (`TelemetryPopoverWindow`) featuring `60S TRACE` real-time chart (`CPU`, `GPU`, `TEMP`), `CPU` / `GPU` / `MEM` / `DISK` hardware cards, and `PROCESSES` live resource table.
  - Single-line transparent desktop corner overlay (`DesktopOverlayWindow`) with lock/unlock drag positioning and automatic `WS_EX_TRANSPARENT` click-through when locked.
- **Four Fixed-Width Skins**: `Hud` (243px), `Fluent` (237px), `Lite` (245px), and `Minimal` (218px), maintaining zero width jitter across load and network speed unit transitions.
- **Built-in `SYS.CONFIG` Settings**: Silent Task Scheduler auto-start (`SysTelemetry_AutoStart`), skin switching, `Auto` / `Dark` / `Light` theme adaptation, polling intervals (`0.5s` / `1.0s` / `2.0s`), temperature threshold (`75°C` / `80°C` / `85°C`), overlay opacity, dock position, horizontal offset, and fullscreen auto-hide.
- **Memory Trim & Visual Feedback**: One-click working-set memory trim button with scale/tilt animation and emerald flash on both the TRIM button and `MEM` card.
- **Global Hotkeys**: `Ctrl+Alt+M` to toggle popover visibility and `Ctrl+Alt+L` to toggle desktop overlay lock/click-through.
- **Zero-SDK Build Script**: Single-command build via `build.ps1` using Windows built-in .NET Framework 4.x `csc.exe`.
