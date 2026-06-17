# BlueAmp — Full Product Roadmap

## Vision
A lightweight Windows Bluetooth audio hub that is transparent about everything — connection state, audio flow, errors — and gradually becomes the one app you need for Bluetooth audio on your PC.

**Core principle:** Every version must stay under 80MB RAM idle. Ship lean, add only what earns its weight.

---

## v1 — Transparent Receiver
*The foundation. Proves the core works and is lightweight.*

**Theme:** Replace the black box with something you can trust.

| Feature | Description |
|---|---|
| Bluetooth audio receiver | Connect any paired phone via A2DP using `AudioPlaybackConnection` |
| Diagnostic dashboard | Every connection event logged with plain-English explanation |
| Audio stream meter | Visual indicator — is audio actually flowing, not just "connected" |
| System tray UI | Small popup panel, dark theme, connection status dot |
| Auto-start + auto-reconnect | Starts with Windows, remembers last device |

**Success:** App idle under 50MB RAM, popup opens under 200ms, zero "unknown error" states shown to user.

---

## v2 — Smart Behavior
*Turn the app from a receiver into something that works with you.*

**Theme:** The app should know what you're doing and act accordingly.

| Feature | Description |
|---|---|
| PC as Bluetooth Speakerphone | Answer phone calls through PC mic + speakers (HFP profile). Overlay appears on incoming call. |
| Smart Automations | Event-triggered actions. "When phone connects → pause Spotify, set PC volume to 40%." |
| Automation triggers | Device connected, device disconnected, audio started, audio stopped, call started, call ended |
| Automation actions | Set volume, mute/unmute, launch app, run script, send Windows notification |
| Automation UI | Simple "if this → then that" rule builder in settings panel |

**Risk to validate before building:** HFP speakerphone without driver-level code. Spike this first — if it requires a kernel driver, HFP moves to vX.

**Success:** User can answer a phone call through their PC without touching their phone. At least 3 useful automations work end-to-end.

---

## v3 — Audio Hub
*Multiple devices, simultaneous audio, the "hub" moment.*

**Theme:** Your PC becomes the center of all your Bluetooth audio.

| Feature | Description |
|---|---|
| Multi-device Hub | Connect up to 3 Bluetooth devices simultaneously, all audio plays together |
| Per-device volume | Independent volume control for each connected device in the popup |
| Device priority | Set which device "wins" when two play at once, or mix them |
| Presence Detection (opt-in) | RSSI-based proximity. Walk away with phone → PC locks. Off by default. |
| Connection profiles | Named presets. "Gaming" = phone audio to headphones. "Meeting" = PC audio only. One-click switch. |

**Technical note:** Validate that `AudioPlaybackConnection` handles 3 simultaneous connections without audio glitching before designing the UI. Test on real hardware first.

**Success:** Two phones connected simultaneously, both playing audio without gaps or glitches. Presence detection opt-in with a clear warning about battery impact.

---

## vX — Add-Ons (Separate Installs)
*Features that earn their own install because they require extra dependencies.*

These are intentionally NOT bundled into the main app to preserve the lightweight footprint.

### Add-On 1: OBS / Discord Audio Bridge
**What:** Makes phone Bluetooth audio appear as a named virtual audio device ("BlueAmp Phone Input") in OBS, Discord, Teams, and any WASAPI-aware app.

**Why it's an add-on:** Requires a virtual audio driver (either bundled VB-Cable dependency or a custom WDM driver). Adds ~10-20MB and a driver install step — wrong for the main app, right for a separate installer.

**How it works:** App installs a virtual audio device. When a phone is connected via BlueAmp, audio is also mirrored to "BlueAmp Virtual Input". OBS picks it up like any microphone.

**Target user:** Streamers, podcast hosts, remote workers who want phone audio in their recording setup.

---

### Add-On 2: Audio Recorder *(future consideration)*
Record incoming Bluetooth audio with timestamp markers and "save last 30 seconds" quick capture. Separate install because of codec/format handling complexity.

---

## Technical Debt Checkpoints

After each version ships, before starting the next:

- [ ] Memory baseline still under 80MB idle with new features
- [ ] All new background services are event-driven (no polling loops)
- [ ] Connection error messages cover every new failure mode introduced
- [ ] Startup time still under 3 seconds cold

---

## What This App Will Never Do

These are out of scope permanently, not just deferred:

- **Real-time transcription** — Whisper.NET (~100MB+) violates the lightweight constraint
- **EQ / audio effects** — Users have dedicated tools; we're a router, not a processor
- **Android app companion** — Bluetooth stack is enough; no need for a paired mobile app
- **Bluetooth file transfer** — Completely different use case
- **LDAC / aptX HD codec support** — Windows `AudioPlaybackConnection` API caps at SBC; unlocking better codecs requires bypassing the Windows Bluetooth stack entirely (a different product)
