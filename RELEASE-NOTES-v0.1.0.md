# Razer Chroma · WLED Studio v0.1.0

First release of the rebuilt bridge between Razer Chroma Broadcast and WLED controllers.

## Highlights

- **4 to 32 palette channels.** The four colours Synapse broadcasts stay the anchors; every
  extra channel is interpolated and labelled `virtual`. No generated colour is ever presented
  as real Razer data.
- **Long strips actually work.** DRGB is used up to 490 LEDs, DNRGB with absolute start
  indexes above that. 1000 LEDs = 3 packets, 2000 LEDs = 5 packets per frame, verified byte
  for byte against the WLED protocol.
- **Proper segment mapping.** Absolute LED positions, gaps between segments stay black,
  per segment reverse and per segment channel range.
- **Smooth by construction.** The Razer callback only copies four colours into a single
  slot; a background render thread maps and sends. The UI never sits in the light path.
- **New UI.** Overview / Devices / Mapping / Performance / Settings, a palette drawn as one
  colour bar, per device LED previews and a tray icon.

## What changed from the original app

See `UPGRADE-NOTES.md` (Chinese) for the full list. The short version: the colour pipeline,
UDP transport, mapping and threading were rebuilt; every original feature (Razer sync,
Lenovo keyboard, multiple WLED devices, run at boot, mDNS discovery) is still there.

## Requirements

- Windows 10 / 11, **.NET 8 Desktop Runtime**
- WLED **0.11 or newer**
- A valid Razer Chroma Broadcast App ID

## Install

1. Download `Razer-Chroma-WLED-Studio-v0.1.0-win-x64.zip`.
2. Extract anywhere and run `RazerChromaWLEDConnect.exe`.
3. Paste your App ID in Settings, then add controllers on the Devices page.

`settings.xml` is created next to the executable on first save; it is not part of the zip.

## Known limitations

- WLED receives live data over UDP; a controller that goes offline is retried every
  30 seconds rather than instantly.
- `ChromaLink1` exists in the SDK but Synapse leaves it black on the tested setups, so only
  ChromaLink2–5 are used as anchors.
- Preview refresh rate is configurable (5–60 fps). Static scenes cost nothing because frames
  are only pushed when the colours actually change.
