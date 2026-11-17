# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

**BlueAmp** is a Windows system-tray app that routes phone audio to a PC over Bluetooth A2DP. It has a transparent diagnostics layer that surfaces real error causes instead of generic failures.

Single-project solution: `src/BlueAmp/BlueAmp.csproj` — .NET 8, WinForms, targets `net8.0-windows10.0.19041.0`. The solution file is `BlueAmp.slnx`.

## Build & run

```powershell
# Debug build and run
dotnet run --project src/BlueAmp/BlueAmp.csproj

# Release build to ./publish (framework-dependent single file)
dotnet publish src/BlueAmp/BlueAmp.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Build installer (requires Inno Setup 6 installed)
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\BlueAmp.iss /DAppVersion=1.0.0 /DPublishDir=..\publish
```

## Release process

Pushing a tag like `v1.0.0` triggers the GitHub Actions release pipeline (`.github/workflows/release.yml`), which publishes the app, builds the installer with Inno Setup, and attaches it to a GitHub Release.

## Architecture

### Layer structure

```
Program.cs
└── TrayApplicationContext (Views/)   — app root, owns all services and windows
    ├── Services/
    │   ├── BluetoothService          — DeviceWatcher over AudioPlaybackConnection selector
    │   ├── AudioConnectionService    — A2DP connect/disconnect, auto-reconnect
    │   ├── AudioMeterService         — 100 ms peak polling via Core Audio (UI thread only)
    │   ├── DiagnosticService         — in-memory log (500 entries, verbose opt-in)
    │   └── SettingsService           — JSON settings + startup registry entry
    ├── ViewModels/
    │   ├── TrayPopupViewModel        — main state machine (device list, connection state, waveform)
    │   ├── DiagnosticViewModel
    │   └── SettingsViewModel
    └── Views/
        ├── PopupForm                 — borderless GDI+ form, all layout/drawing is manual
        ├── SettingsForm
        ├── DiagnosticsForm
        └── MessageWindow             — hidden HWND for WM_POWERBROADCAST (sleep/wake)
```

### Key design decisions

**Thread marshaling** — `Helpers/Ui.cs` is the single marshal point (replaces WPF's Dispatcher). `Ui.Post()` is fire-and-forget; `Ui.Send()` blocks. All UI-bound callbacks go through one of these. Prefer `Post` from WinRT callback threads (DeviceWatcher, AudioPlaybackConnection.StateChanged) so they never block on the UI. `AudioConnectionService` state is UI-thread-only — its connection-drop callback posts over before touching anything.

**Core Audio interop** — `Interop/CoreAudio.cs` contains hand-rolled COM bindings for MMDevice API (IMMDeviceEnumerator, IAudioMeterInformation, IAudioEndpointVolume, IPropertyStore). This replaced the NAudio dependency to keep the footprint small. `AudioEndpoint` and `AudioDevices` (in `Interop/AudioEndpoint.cs`) are the entry points. **All COM calls must happen on the UI thread** — the `AudioMeterService` uses a WinForms timer to enforce this.

**AVRCP handshake** — `Helpers/AudioStreamActivator.cs` sends an invisible 1% volume dip after `OpenAsync()` completes to trigger the phone's AVRCP SET_ABSOLUTE_VOLUME and start streaming. Some phones won't stream without this.

**PopupForm rendering** — the popup is a fully custom GDI+ form (no child controls). All layout is computed in `MeasureHeight()` / `Relayout()` and all drawing in `OnPaint`. Hit-testing uses a `_hotspots` list built each paint pass. DPI scaling uses `_scale = DeviceDpi / 96f` applied via the helper `S(int)`.

**Memory footprint** — workstation GC (non-concurrent, non-server) is set in the csproj. `MemoryHelper.TrimNow()` (called on popup close) trims the working set back to the OS.

### Settings

Stored in `%APPDATA%\BlueAmp\settings.json`. `AppSettings` fields: `LaunchAtStartup`, `RememberLastDevice`, `AutoReconnect`, `KeepLogHistory` (verbose logging), `LastDeviceId`. Writes go through a temp file + swap so a crash mid-save can't corrupt the file.

### Diagnostics

`DiagnosticService` keeps up to 500 entries. By default, only `Error` level entries are captured — `Info`/`Success`/`Warning` are dropped unless `VerboseEnabled` is true (set from the "Enable diagnostic logging" toggle in Settings). This keeps background thread logging cost near zero in normal operation. Fatal/unhandled exceptions are also appended to `%APPDATA%\BlueAmp\crash.log` (`Helpers/CrashLog.cs`).

### Tests and CI

`tests/BlueAmp.Tests` (xUnit) is part of `BlueAmp.slnx`; run with `dotnet test BlueAmp.slnx`. `.github/workflows/build.yml` builds and tests on every push and PR.
