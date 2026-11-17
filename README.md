# BlueAmp

A small Windows tray app that turns your PC into a Bluetooth speaker for your phone, and tells you plainly what's wrong when it doesn't work.

Pair your phone with the PC once, click **Connect** in BlueAmp, and whatever plays on the phone comes out of the PC's speakers or headphones.

> [!WARNING]
> **Personal project: it may not work with your devices.**
> BlueAmp was built for my own use and has only been tested on my own PC and phones. It has not been tested or tuned for the wide range of Bluetooth adapters, drivers, phones and Windows setups out there. It may fail to connect, stutter, or behave differently on your hardware. It is provided as-is, with no warranty and no guarantee of support.

## Features

- **Phone → PC audio over Bluetooth.** Uses the A2DP support built into Windows (`AudioPlaybackConnection`), so no drivers or phone app are needed.
- **Lives in the tray.** Click the icon for a small popup with your paired phones and a Connect button.
- **Shows what's really happening.** Sample rate, bit depth, channels and a live waveform, so you can see whether audio is actually flowing and not just "connected".
- **Real error messages.** When a connection fails you get a specific reason, or at least the Windows error code, instead of "unknown error".
- **Auto-reconnect.** Reconnects after a dropout and after the PC wakes from sleep, and can reconnect to your last phone when Windows starts.
- **Lightweight.** Uses little memory and does nothing in the background while idle.
- **Diagnostics window.** An optional detailed activity log you can copy and share.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- A Bluetooth adapter in the PC
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0); the installer offers to open the download page if it's missing
- A phone already paired with the PC in Windows Bluetooth settings

## Install

1. Download `BlueAmp-Setup-<version>.exe` from the [Releases](../../releases) page.
2. Run it. It installs for your user account only and needs no admin rights.
3. BlueAmp starts in the system tray (check the `^` overflow area if you don't see it).

## Use

1. Pair your phone with the PC in **Windows Settings → Bluetooth & devices** if you haven't already.
2. Click the BlueAmp tray icon.
3. Click **Connect** next to your phone.
4. Play something on the phone.

Right-click the tray icon for **Settings**, **Diagnostics** and **Exit**.

## Troubleshooting

| Problem | Try |
|---|---|
| Phone isn't in the list | Make sure it's paired in Windows Bluetooth settings, not just discovered. |
| Connected but no sound | Start playback on the phone, and check its Bluetooth settings for this PC to make sure **Media audio** is on. |
| Connection fails | Read the message in the popup. Turning the phone's Bluetooth off and on often helps. If it repeats, open **Diagnostics** and copy the log. |
| Audio stutters | Usually radio congestion, not distance. Avoid using Bluetooth headphones on the same PC at the same time. If you route audio through Voicemeeter or a similar mixer, try playing straight to your speakers. |
| App crashed | Details are saved in `%APPDATA%\BlueAmp\crash.log`. |

Settings are stored in `%APPDATA%\BlueAmp\settings.json`.

## Build from source

Requires the .NET SDK 9 or later (the solution file uses the `.slnx` format; the app itself targets .NET 8).

```powershell
# Run in debug
dotnet run --project src/BlueAmp/BlueAmp.csproj

# Run the tests
dotnet test BlueAmp.slnx

# Release build into ./publish
dotnet publish src/BlueAmp/BlueAmp.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Build the installer (requires Inno Setup 6)
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\BlueAmp.iss /DAppVersion=1.0.0 /DPublishDir=..\publish
```

Pushing a tag such as `v1.1.0` makes GitHub Actions build the installer and publish a release. Every push runs the build and tests.

## Known limitations

- **Audio quality is set by Windows.** Its Bluetooth receiver uses the SBC codec, so higher-quality codecs (AAC, aptX, LDAC) aren't available.
- **One phone at a time.**
- **Audio goes one way only: phone → PC.**
- **Windows only.**

## Docs

- [Roadmap](docs/roadmap.md)
- [Original idea](docs/ideas/blueamp.md)
- [v1 build plan](docs/v1-plan.md)

## License

BlueAmp is released under the [MIT License](LICENSE). You're free to use, modify and share it, provided the copyright notice is kept. It comes **as-is, with no warranty**.

BlueAmp uses [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT, © .NET Foundation and Contributors). Full notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

"Bluetooth" is a trademark of Bluetooth SIG, Inc. BlueAmp is an independent project, not affiliated with or endorsed by Bluetooth SIG, Microsoft or any phone maker.
