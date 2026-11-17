# V1 Build Plan โ€” BlueAmp

## Goal
Ship a lightweight Windows system tray app that receives Bluetooth audio from a phone and shows users exactly what's happening โ€” why it connected, why it didn't, whether audio is actually flowing.

## Tech Stack

| Layer | Choice | Why |
|---|---|---|
| Language | C# .NET 8 | WinRT APIs work natively, good BT library support |
| UI Framework | WPF | Lighter than WinUI 3, battle-tested for tray apps, .NET 8 supported |
| Tray | H.NotifyIcon.Wpf | Best modern WPF tray library, supports popup windows |
| Bluetooth | Windows.Devices.Bluetooth (WinRT) | Official API for device enumeration and pairing state |
| Audio Connection | Windows.Media.Audio.AudioPlaybackConnection (WinRT) | Official A2DP Sink API, no driver needed |
| Audio Metering | NAudio (WASAPI loopback) | Detect if audio is actually flowing from the BT device |
| MVVM | CommunityToolkit.Mvvm | Lightweight, source-generator based, minimal overhead |

**Minimum Windows version:** Windows 10 2004 (Build 19041) โ€” required for `AudioPlaybackConnection`.

---

## Project Structure

```
BlueAmp/
โ”โ”€โ”€ src/
โ”   โ””โ”€โ”€ BlueAmp/
โ”       โ”โ”€โ”€ BlueAmp.csproj
โ”       โ”โ”€โ”€ App.xaml / App.xaml.cs         # Entry point, tray lifecycle
โ”       โ”โ”€โ”€ Views/
โ”       โ”   โ”โ”€โ”€ TrayPopup.xaml             # Main popup panel (click tray icon)
โ”       โ”   โ””โ”€โ”€ DiagnosticWindow.xaml      # Expandable log panel
โ”       โ”โ”€โ”€ ViewModels/
โ”       โ”   โ”โ”€โ”€ TrayPopupViewModel.cs      # Device list, connect/disconnect, status
โ”       โ”   โ””โ”€โ”€ DiagnosticViewModel.cs     # Log entries, error explanations
โ”       โ”โ”€โ”€ Services/
โ”       โ”   โ”โ”€โ”€ BluetoothService.cs        # Device discovery, pairing state
โ”       โ”   โ”โ”€โ”€ AudioConnectionService.cs  # AudioPlaybackConnection lifecycle
โ”       โ”   โ””โ”€โ”€ AudioMeterService.cs       # WASAPI loopback, stream level
โ”       โ”โ”€โ”€ Models/
โ”       โ”   โ”โ”€โ”€ BluetoothDeviceInfo.cs     # Device name, ID, paired state
โ”       โ”   โ””โ”€โ”€ DiagnosticEntry.cs         # Log entry with timestamp + plain text
โ”       โ””โ”€โ”€ Helpers/
โ”           โ””โ”€โ”€ ErrorTranslator.cs         # Maps API errors โ’ plain English
โ”โ”€โ”€ docs/
โ”   โ”โ”€โ”€ ideas/blueamp.md
โ”   โ”โ”€โ”€ v1-plan.md                         # This file
โ”   โ””โ”€โ”€ roadmap.md
โ””โ”€โ”€ README.md
```

---

## Build Tasks

### Task 1 โ€” Project Scaffolding
**Goal:** Runnable blank WPF app that starts in the tray and exits cleanly.

- [ ] Create solution + WPF project targeting `net8.0-windows10.0.19041.0`
- [ ] Add NuGet packages: `H.NotifyIcon.Wpf`, `NAudio`, `CommunityToolkit.Mvvm`
- [ ] Enable WinRT interop in csproj (`UseWindowsDesktopSdk`, `Platforms`)
- [ ] Hide main window on startup, show only tray icon
- [ ] Right-click tray menu: "Open", "Settings", "Exit"
- [ ] App exits cleanly when "Exit" clicked (no orphan processes)

**Done when:** App appears in system tray, right-click works, exits without error.

---

### Task 2 โ€” Bluetooth Device Discovery
**Goal:** Show a list of paired Bluetooth audio devices in the popup.

- [ ] Enumerate paired devices using `DeviceInformation.FindAllAsync` with `AudioPlaybackConnection.GetDeviceSelector()`
- [ ] Filter for devices that support A2DP (audio profile)
- [ ] Watch for devices added/removed via `DeviceWatcher`
- [ ] Expose device list to `TrayPopupViewModel`
- [ ] Show device name + current paired/unpaired state

**Done when:** Opening the popup shows real paired Bluetooth devices from the system.

---

### Task 3 โ€” Audio Connection (Connect / Disconnect)
**Goal:** Click a device โ’ audio from phone plays on PC.

- [ ] Create `AudioPlaybackConnection` for selected device
- [ ] Call `StartAsync()` and handle all result states:
  - `Success` โ’ mark as connected
  - `RequestTimedOut` โ’ translate to "Your phone took too long to respond. Make sure Bluetooth is on."
  - `DeniedBySystem` โ’ translate to "Windows blocked the connection. Try unpairing and re-pairing your phone."
  - `UnknownFailure` โ’ translate to "Something went wrong. Check the diagnostic log for details."
- [ ] `OpenAsync()` to start audio stream
- [ ] Disconnect: dispose `AudioPlaybackConnection` cleanly
- [ ] Handle phone disconnecting mid-session (device watcher event)

**Done when:** Phone audio plays through PC speakers after clicking Connect. Disconnect works cleanly.

---

### Task 4 โ€” Diagnostic Log
**Goal:** Every connection event is logged with a timestamp and plain-English explanation.

- [ ] Create `DiagnosticEntry` model: timestamp, event type, raw status, human message
- [ ] Log every state change: Connecting โ’ Connected โ’ Streaming โ’ Disconnected โ’ Error
- [ ] Map `AudioPlaybackConnectionOpenResultStatus` to human messages (via `ErrorTranslator`)
- [ ] Show last 3 entries in the tray popup (collapsed)
- [ ] "View full log" button opens `DiagnosticWindow` with full scrollable history
- [ ] Log persists for the session (cleared on app restart)

**Done when:** Every connection attempt produces at least one readable log entry explaining what happened.

---

### Task 5 โ€” Audio Stream Meter
**Goal:** Visual indicator showing whether audio is actually flowing (not just "connected").

- [ ] Use NAudio WASAPI loopback to capture the BT device audio endpoint
- [ ] Sample peak level every 100ms
- [ ] Expose as a 0โ€“100 float to the ViewModel
- [ ] Display as a small animated bar in the tray popup
- [ ] Three states: Silent (grey), Low signal (green), Active (bright green)
- [ ] Meter stops when disconnected

**Done when:** The bar moves when music plays on the phone, stays grey when connected but silent.

---

### Task 6 โ€” Tray Popup UI
**Goal:** A clean, small popup panel that shows everything at a glance.

Layout:
```
โ”โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”
โ” BlueAmp              โ—      โ”  โ status dot (grey/green)
โ”โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”ค
โ” Samsung Galaxy S24    [Connect] โ”
โ” AirPods Pro           [Connect] โ”
โ”โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”ค
โ” โ–“โ–“โ–“โ–“โ–“โ–‘โ–‘โ–‘โ–‘โ–‘  Audio flowing  โ”  โ stream meter
โ”โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”ค
โ” Last event: Connected (2s ago)  โ”
โ” [View Diagnostic Log]       โ”
โ””โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”€โ”
```

- [ ] WPF popup anchored to tray icon (bottom-right of screen)
- [ ] Auto-closes when clicking outside
- [ ] Tray icon changes color: grey = idle, green = connected, red = error
- [ ] Status dot in popup mirrors tray icon color
- [ ] Dark theme by default

**Done when:** UI matches layout above, feels snappy to open/close.

---

### Task 7 โ€” Settings & Startup
**Goal:** Basic settings that make the app actually usable day-to-day.

- [ ] "Launch at Windows startup" toggle (writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
- [ ] "Remember last connected device" โ€” auto-connects on app start
- [ ] Settings stored in `%AppData%\BlueAmp\settings.json`
- [ ] Settings accessible from tray right-click menu

**Done when:** App starts with Windows, remembers the last device, auto-connects.

---

### Task 8 โ€” Polish & Validation
**Goal:** App feels solid enough to use daily without babysitting it.

- [ ] Memory baseline under 50MB idle
- [ ] CPU usage under 1% when idle (no audio streaming)
- [ ] Test: phone connects, music plays, phone disconnects, reconnect works
- [ ] Test: app survives PC sleep/wake with auto-reconnect
- [ ] Test: app survives Bluetooth adapter disable/enable
- [ ] No crash after 8 hours idle in tray

**Done when:** All test scenarios pass. Ship it.

---

## V1 Success Criteria
1. App uses under 50MB RAM idle
2. Tray popup opens in under 200ms
3. Every connection failure produces a readable explanation (not "unknown error")
4. Audio meter correctly shows silence vs. active stream
5. App auto-starts and auto-reconnects to last device
