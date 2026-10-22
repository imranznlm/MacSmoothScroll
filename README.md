# MacSmoothScroll

A small Windows tray utility that gives mouse-wheel scrolling the same inertia
feel as a Mac trackpad. It works system-wide in any app.

## Download

Grab the latest single-file build from
**[Releases](https://github.com/imranznlm/MacSmoothScroll/releases/latest)** —
one `MacSmoothScroll.exe`, no installer and no .NET runtime required.

1. Download `MacSmoothScroll.exe` and put it anywhere you like (Desktop is fine).
2. Run it. Windows may show SmartScreen once (unsigned exe): **More info → Run anyway**.
3. It has no main window — look for the chevron icon in the notification area.
   Right-click it for **Settings...**, **Pause smooth scrolling** and **Exit**.

Requires Windows 10/11 (x64). The app stores its settings in
`%LOCALAPPDATA%\MacSmoothScroll\settings.json` and starts with Windows if you
enable the switch in Settings.

## What it does

A physical wheel notch is swallowed and replaced by a short stream of smaller
wheel steps that decay exponentially, so scrolling glides instead of jumping.

- Works in every app, including apps that scroll natively (Chromium, Explorer, Office).
- Per-app exclusions: listed apps keep their normal scrolling.
- `Ctrl+wheel` (zoom) and `Alt+wheel` are passed through untouched.
- Horizontal wheel (`WM_MOUSEHWHEEL`) gets the same smoothing.
- Tray menu: **Pause smooth scrolling**, **Settings...**, **Exit** (double-click opens settings).
- Single instance: a second copy exits with a note instead of double-hooking the mouse.

## How it works

```
physical wheel event
  -> MouseHook (WH_MOUSE_LL) swallows it, looks up the app under the cursor
  -> ScrollAnimator (own thread, 16 ms tick) integrates velocity and decays it
  -> SendInput(MOUSEEVENTF_WHEEL / MOUSEEVENTF_HWHEEL) re-emits the smoothed steps
```

Injected events are filtered out in the hook (`LLMHF_INJECTED`), so the animator
never re-processes its own output. Process-name lookups never block the hook:
unknown apps are resolved on a background thread and treated as "not excluded"
until the answer arrives.

## Settings

Open **Settings...** from the tray menu (or double-click the tray icon). The
window is a borderless card that follows the Windows light/dark theme, and every
change applies immediately - there is no Save button to forget. Press `Esc` or
click **Done** to close; values are stored on close and on exit.

- **Presets** - *Subtle*, *Balanced* and *Fast* set glide and speed in one click.
- **Glide length** - how far the page keeps moving after you stop turning the wheel.
- **Speed** - multiplier for every wheel notch.
- **Advanced** - reveals the fine-tuning sliders (minimum step, speed cap, update rate).
- **Excluded apps** - listed apps keep their normal scrolling. Add a name by hand,
  or use **Add app under cursor...** and hover the target window for 3 seconds.
  Select an entry and press `Delete` (or **Remove**) to stop excluding it.
- **Start with Windows** - adds or removes the app from your sign-in startup list.
  It writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MacSmoothScroll`,
  the same entry Task Manager shows under *Settings > Apps > Startup*.

Both themes are drawn with the same palette; the icon in the title bar, the
window icon and the tray icon are all the embedded multi-resolution `app.ico`.

Stored in `%LOCALAPPDATA%\MacSmoothScroll\settings.json`.

| Setting | Default | Meaning |
|---|---|---|
| `Decay` | `0.82` | Inertia. Higher = longer glide. |
| `Sensitivity` | `1.2` | Speed multiplier for incoming wheel deltas. |
| `MinChunk` | `12` | Smallest step emitted, so the glide never feels stuck. |
| `MaxVelocity` | `1200` | Cap on accumulated speed, so long spins cannot coast forever. |
| `IntervalMs` | `16` | Animation tick (~60 FPS). Inertia is normalized, so this only changes smoothness. |
| `Blacklist` | `[]` | Process names that keep native scrolling (no `.exe` needed). |

Older settings files load fine; new fields fall back to their defaults.

## Build

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

The runnable app is `bin\Release\net10.0-windows\win-x64\publish\MacSmoothScroll.exe`
(self-contained, no .NET install required). It has no main window: after starting,
look for the icon in the notification area.

### One file to share

To copy the app to another PC, publish it as a single self-contained file:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o .\dist
```

That produces `dist\MacSmoothScroll.exe` (~50 MB) — copy just that file to the other
PC and run it; no .NET install or extra DLLs are needed. On Windows 11 ARM devices,
use `-r win-arm64` instead of `-r win-x64`. The exe is unsigned, so SmartScreen may
ask for **More info → Run anyway** the first time.

## Repository layout

- `Program.cs` — tray app, settings model, animator, mouse hook, window probe and the embedded app icon.
- `Ui/Theme.cs` — light/dark palette, rounded-rect helpers and `RoundedPanel`.
- `Ui/SliderControl.cs` — the custom accent slider used by the settings window.
- `Ui/SettingsForm.cs` — the settings card: presets, sliders, exclusions, live apply.
- `app.ico` — the app icon (16-256 px). Shipped as the exe icon and embedded as a resource.
- `MacSmoothScroll.csproj` — `net10.0-windows`, WinForms, nullable enabled.
- `SmoothScrollwindows/` — a second checkout of the same code with its own git history
  (`origin: github.com/imranznlm/SmoothScrollwindows.git`). It is excluded from this
  project's build; keep the copies in sync or delete one of them.
