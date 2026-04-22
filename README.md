# HID Fader

A Windows volume mixer that maps **HID controllers** to **per-application volume control**. 
Turn any knob, slider, hat or button on your gaming device into a volume fader for your favorite game or application.
That way you can control volume of your game, voice chat app, music player...

![Downloads](https://img.shields.io/github/downloads/alexunder18/HIDFader/total)
![License](https://img.shields.io/github/license/alexunder18/HIDFader)

## ✨ Features

- **Per-application volume control** — bind inputs to a specific process (e.g. `DCS.exe`, `discord.exe`, `spotify.exe`) via the Windows audio session API.
- **HID / DirectInput support** — buttons, POV hats, and analog axes from any DirectInput game controller.
- **Three actions** — Volume Up, Volume Down, Mute toggle.
- **Analog axis mode** — map a throttle or slider directly to a volume level (with optional axis inversion for hardware that reports the wrong direction).
- **Modifier keys** — Acts as a Shift button, so you can use same buttons for controlling different application volume levels. You can also have multiple modifiers binded at once.
- **Device-stable bindings** — HID devices are identified by a VID/PID-derived GUID, so bindings survive USB port changes and reboots.
- **Configurable volume change sensitivity** (1–10% per tick).
- **Single-file executable** — packaged with Fody/Costura, no install required.

## 📋 Requirements

- Windows 10 / 11
- .NET Framework 4.8
- Any DirectInput-compatible HID controller.
- Tested with Thrustmaster Warthog Throttle and HOTAS, Saitek Cyborg, and some random USB Game Pad

## 🔨 Build

Open `HIDFader.sln` in Visual Studio 2019+ and build the `Release` configuration, or from the command line:

```
msbuild HIDFader.sln /p:Configuration=Release
```

NuGet packages (CSCore, SharpDX.DirectInput, NLog, Fody, Costura.Fody) are restored automatically.

The output is a single `HIDFader.exe` in `bin\Release\`.

## 🚀 Usage

1. Launch `HIDFader.exe`.
2. All applications that are visible in the `Windows Volume Mixer` will be visible in the list.
   (If you open your app after the HIDFader, click refresh to update the list)
4. Open the binding editor for the application and bind inputs to **Volume Up**, **Volume Down**, or **Mute**.
5. Adjust the global volume step (1–10%) as needed.
6. Leave it running in the background, or minimize the app to tray.

### Application selector
Double click the application from the list, click `Configure` or use keyboard and hit enter to open Binding editor
<img width="586" height="493" alt="image" src="https://github.com/user-attachments/assets/e7f634b3-7a5f-4aab-8b92-b9c9000aa944" />

### Binding editor
Click on the input or modifier to start listening for a bind. Click checkmark icon to confirm or X to clear  
Use  `Apply & Save` to save the changes or `Reset All` to clear all bindings
<img width="632" height="577" alt="image" src="https://github.com/user-attachments/assets/dab1be2a-2af2-4c5a-abe4-f488e68c2503" />

Configuration and logs are stored in:

```
%APPDATA%\HIDFader\
```
## 🧰 Tech stack

| Component | Library |
|---|---|
| Audio sessions | [CSCore](https://github.com/filoe/cscore) |
| HID / DirectInput | [SharpDX.DirectInput](https://github.com/sharpdx/SharpDX) |
| Logging | [NLog](https://nlog-project.org/) |
| Single-file packaging | [Fody](https://github.com/Fody/Fody) + [Costura.Fody](https://github.com/Fody/Costura) |
| UI | WinForms |

## 📁 Project layout

```
Core/    Configuration, bindings, audio session management
Input/   DirectInput device enumeration, listener, hold-to-repeat state
UI/      WinForms application selector and binding editor
```

## 📄 License

[MIT](LICENSE)
