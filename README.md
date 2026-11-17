# Blink Break

A tiny, free Windows app that reminds you to rest your eyes. Every 20 minutes it covers your screen for 20 seconds with a gentle **blink break**: an animated eye blinks slowly so you can blink along with it, then you look at something far away.

![Blink Break overlay](docs/screenshot.png)

## Why 20 minutes / 20 seconds?

Eye doctors recommend the **20-20-20 rule** ([American Academy of Ophthalmology](https://www.aao.org/eye-health/tips-prevention/computer-usage)): every **20 minutes**, look at something **20 feet (6 m)** away for **20 seconds**. When you stare at a screen you blink much less than normal, which dries out your eyes. A short break where you blink fully and refocus into the distance helps with that.

Both the interval and the break length can be changed from the tray menu.

## Download

1. Download **`BlinkBreak.exe`** from the [latest release](../../releases/latest).
2. Move it somewhere permanent, for example `C:\Users\<you>\AppData\Local\Programs\BlinkBreak\`. It registers itself to start with Windows from wherever it is the first time it runs.
3. Double-click it. A blue eye icon appears in the system tray (next to the clock, possibly under the `^` arrow).

> **Windows SmartScreen** may warn you because the app isn't code-signed. Click **More info → Run anyway**. The full source is in this repo, and you can build it yourself (see below).

Requires Windows 10 or 11. No installer and no dependencies: it uses the .NET Framework 4.x that ships with Windows.

## Using it

- **During a break:** press **Esc** (or click "Press Esc to skip") if you really need to get out early.
- **Hover or left-click** the tray icon to see when the next break is.
- **Right-click** the tray icon for these options:
  - Take a blink break now
  - Pause for 1 hour
  - Break every 10 / 15 / 20 / 30 / 45 / 60 minutes
  - Break length 10 / 15 / 20 / 30 / 60 seconds
  - Start with Windows (on/off)
  - Exit

It also handles a few situations on its own:

- Covers **every monitor**.
- Restarts the timer when you **unlock** your PC or it **wakes from sleep**, so you don't get a break the moment you come back.
- Puts the break off by a minute while a **full-screen game** or **presentation mode** is running, so you don't get kicked out of the game. Full-screen video still gets breaks.

Settings are stored in `%APPDATA%\BlinkBreak\settings.ini`.

## Uninstall

Right-click the tray icon, untick **Start with Windows**, choose **Exit**, then delete the exe and the `%APPDATA%\BlinkBreak` folder. If you cloned the repo, you can run `uninstall.cmd` instead, which does all of that.

## Build from source

No SDK needed. The C# compiler that comes with Windows is enough:

```bat
git clone https://github.com/OniuUI/blink-break.git
cd blink-break
build.cmd        :: produces bin\BlinkBreak.exe
install.cmd      :: builds, copies to %LOCALAPPDATA%\Programs\BlinkBreak and starts it
```

All the code is in [`src/BlinkBreak.cs`](src/BlinkBreak.cs) (a single file, C# 5 / WinForms). The app draws its own icon, and `build.cmd` embeds it into the exe.

## Disclaimer

Blink Break is a reminder tool, not medical advice. If you have persistent eye strain, dryness, or headaches, see an eye-care professional.

## License

[MIT](LICENSE)
