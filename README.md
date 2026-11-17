# Razer Chroma · WLED Studio

Bridges the colours Razer Synapse broadcasts onto any number of WLED controllers, with a
modern desktop UI, a 4–32 channel palette and real UDP segmentation for long strips.

Forked from [fu-raz/Razer-Chroma-WLED-Connect-App](https://github.com/fu-raz/Razer-Chroma-WLED-Connect-App)
and rebuilt around a testable core plus a WPF shell.

## What it does

- **Chroma input** — reads the four colours Razer broadcasts (ChromaLink2–5) and expands them
  into 4, 8, 12, 16, 24 or 32 palette channels. Native colours are marked as such; everything
  else is an interpolated channel and is labelled `virtual` in the UI.
- **WLED output** — DRGB for strips up to 490 LEDs, automatic DNRGB with absolute start
  indexes above that. A 2000 LED strip is five packets per frame.
- **Multiple controllers** — per device LED window, brightness, palette range, blend,
  direction and per segment mapping with absolute LED positions.
- **Live preview** — one colour bar for the palette plus a per device strip preview drawn
  into a `WriteableBitmap`, decoupled from the light output.
- **Background pipeline** — the Razer callback only copies four colours into a single slot;
  a render thread maps and sends. A slow frame is replaced, never queued.
- **Performance page** — output FPS, Razer frames, packets per second, send latency,
  skipped frames, per device state.

## Requirements

| | |
|---|---|
| OS | Windows 10 / 11 |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Build | .NET SDK 8.0+ or Visual Studio 2022 |
| Controllers | WLED **0.11 or newer** (UDP realtime API) |
| Razer | A valid Chroma Broadcast App ID registered in Synapse |

## Build

Double click **`build.cmd`**. It restores, builds, copies the result to `..\dist`, writes
`release\*.zip` and opens the output folder. The full log ends up in `build.log`.

Or, from a terminal:

```powershell
dotnet build "Razer Chroma WLED Connect App.sln" -c Release
```

## Run

Run `dist\RazerChromaWLEDConnect.exe` (or `bin\Release\net8.0-windows\...` if you built
manually). Closing the window sends it to the tray; **Quit** lives in the tray menu and asks
for confirmation. Only one instance can run at a time — a second launch brings the first
window back.

## First run

1. Open **Settings** and paste your Razer App ID (a full GUID, e.g. `xxxxxxxx-xxxx-...`).
   A truncated or missing ID shows as *App ID rejected* on the Overview page.
2. Open **Devices**, press **Search** to discover controllers on the local network. Discovery
   only lists what it found — nothing is added until you press **Add**. You can also type an
   address by hand.
3. Pick a channel count on the Overview palette.
4. Open **Mapping** to choose which channels each controller uses.

`settings.xml` is created next to the executable and is deliberately **not** in this
repository — it holds your App ID and your device list.

## Project layout

```
RazerChromaWLEDConnect.Core/     no WPF dependency, unit testable
  Core/      Rgb, ColorInterpolationEngine, LedMappingEngine, WledUdpProtocol,
             WledTransport, FrameSlot, ChromaInputManager, RenderPipeline,
             PerformanceMonitor, DeviceManager
  Base/      RGBBase (device contract, legacy settings compatible)
  WLED/      WLEDModule, WLEDSegment
  ViewModels/Main, Device, Channel, Segment, Performance, Settings
RazerChromaWLEDConnect/          WPF shell
  Views/     Overview, Devices, Mapping, Performance, Settings
  Controls/  PaletteStrip, LedBar, LedStrip (WriteableBitmap previews)
  Services/  Chroma link, mDNS discovery, registry, power events
  Theme/     colours and control styles
```

## Compatibility

Existing `settings.xml` files from the original app load unchanged. The old four
`Led1..Led4` ticks map to C1..C4 exactly as before, and only keep that meaning while the
palette still has four channels.

## Credits

- Original app: [fu-raz](https://github.com/fu-raz/Razer-Chroma-WLED-Connect-App)
- Chroma wrapper: [ChromaBroadcastSDK.NET](https://github.com/ChromaControl/ChromaBroadcastSDK.NET)
- WLED: [Aircoookie/WLED](https://github.com/Aircoookie/WLED)
- Icon: Led lighting icons by Smashicons, Flaticon

## Notes

- See `TEST-REPORT.md` for the protocol and mapping test results (Chinese).
- See `UPGRADE-NOTES.md` for a detailed change log against the original project (Chinese).
- Do not commit `settings.xml`: `.gitignore` already excludes it.
