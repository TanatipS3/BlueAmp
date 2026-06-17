# BlueAmp — Idea One-Pager

## Problem Statement
How might we build a lightweight Windows Bluetooth audio receiver that exposes what's actually happening — so users stop guessing why their audio isn't working — and grows into a genuinely useful audio hub?

## Recommended Direction
A system tray app built on C# .NET 8 + WPF using the `AudioPlaybackConnection` WinRT API (Windows 10 2004+). The app lives entirely in the tray — click to open a small popup panel, nothing more. Everything is event-driven, no polling, zero idle cost except when Presence Detection is explicitly turned on.

The core insight: every existing app is a black box by accident, not design. The Windows API already exposes the connection state machine — nobody has wrapped it in a human-readable UI. That's the wedge. Everything else (speakerphone, automations, multi-device) is built on top of that honest foundation.

## Key Assumptions to Validate
- [ ] `AudioPlaybackConnection` supports multiple simultaneous connections cleanly — test with 2 devices before committing to Multi-device Hub
- [ ] HFP speakerphone can be managed from app-level code without touching driver layer — spike this early, it's the highest-risk feature
- [ ] WPF tray popup feels snappy enough to feel "lightweight" — test startup time and memory baseline before adding features

## MVP Scope
**In for v1:**
- System tray icon with connection status indicator
- Click → small popup: device list, connect/disconnect button, live audio stream meter, plain-English connection status
- Diagnostic log panel (what happened, why it failed)
- Single device connection

**Not in v1:**
- Speakerphone, automations, presence, multi-device — all v2+

## Not Doing (and Why)
- **Smart Audio Mixer** — weight and complexity contradict the lightweight goal; routing belongs in dedicated tools
- **Real-time Transcription** — Whisper.NET adds 100MB+ to the binary; violates the lightweight constraint
- **EQ / Effects** — scope creep; users have dedicated tools for this
- **OBS/Discord routing (core)** — kept as future add-on; the virtual driver requirement is a separate complexity tier
- **Tier 3 features (recording, hotkey center, profiles)** — nice, not necessary; add after core is proven

## Feature Roadmap
```
v1  → Core receiver + diagnostic dashboard
v2  → Speakerphone + Smart Automations
v3  → Multi-device Hub + Presence Detection (opt-in)
vX  → OBS/Discord add-on (separate install)
```

## Open Questions
- WPF vs WinUI 3 — WPF is lighter and battle-tested for tray apps; WinUI 3 is modern but heavier.
- What's the target minimum Windows version? Windows 10 2004 is the floor for `AudioPlaybackConnection`.
- Does speakerphone (HFP) move to v1 or stay v2? If the main pain is phone calls, it changes the build order.
