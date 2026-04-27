# HIDMate

Control volume, keyboard, and mouse using your **HID devices**.

Turn any knob, slider, hat, or button on your controller into a way to control your apps — whether it's audio, keyboard input, or even mouse movement.

![Downloads](https://img.shields.io/github/downloads/alexunder18/HIDMate/total)
![License](https://img.shields.io/github/license/alexunder18/HIDMate?branch=develop&cacheSeconds=0)

---

## ✨ Features

- **Per-application volume control** — bind inputs to a specific process (e.g. `DCS.exe`, `discord.exe`, `spotify.exe`)
- **Keyboard bindings** — map HID inputs to keyboard keys for the selected application (app must be in focus)
- **Mouse bindings (global)** — control mouse movement, clicks, and scrolling
- **HID / DirectInput support** — buttons, POV hats, and analog axes from any compatible controller
- **Analog axis support** — map throttles or sliders directly to values (e.g. volume or movement)
- **Modifier buttons** — reuse the same controls for different actions depending on which modifiers are pressed
- **Configurable sensitivity** — fine-tune how inputs behave
- **Single-file executable** — no installation required

---

## 📋 Requirements

- Windows 10 / 11  
- .NET Framework 4.8  
- Any DirectInput-compatible HID controller  
- Tested with Thrustmaster Warthog Throttle and HOTAS, Saitek Cyborg, and generic USB gamepads  

---

## 🚀 Usage

1. Launch `HIDMate.exe`
2. Applications visible in the **Windows Volume Mixer** will appear in the list  
   (If you open an app later, click refresh)
3. Open the binding editor for an application  
4. Bind inputs for:
   - Volume control
   - Keyboard actions
5. Configure mouse bindings (global)
6. Minimize to tray and use it in the background  

---

### Main screen

Double-click an application, click `Configure`, or press Enter to open the binding editor.

<img alt="image" src="/mainscreen.png" />

---

### Binding editor

On this page, you can configure Volume and Keyboard bindings. Note that keyboard bindings will fire only if application is in focus, otherwise will be ignored.

Click on an input or modifier to start listening for a bind.  
Use the checkmark to confirm or X to clear.  
Click `Apply & Save` to save changes or `Reset All` to clear everything.

<img alt="image" src="/volumebindings.png" />
<img alt="image" src="/keyboardbindings.png" />

---

### Mouse binging

You can use any button or axis for controlling your mouse pointer (directions, left/right buttons, scroll up/down)
<img alt="image" src="/mousebindings.png" />

## 📁 Configuration

Configuration and logs are stored in:
```%appdata%\HIDMate```

If you have existing configuration from previous version, located in ```%appdata%\HIDFader```, just move it to the new location
