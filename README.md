# MFD Extended

An additive "second world" for an existing RasterPropMonitor / MOARdV's Avionics Systems (MAS) multi-function display, reachable with a button
press, exitable the same way, without replacing, renaming, or otherwise disturbing anything the host IVA already does.

## What it does

- **A shared module bay inside an existing MFD**, not a new prop of its own: from the host's own home page, press NEXT or PREV (either one works) to enter.
- **Never touches the host's own pages or buttons** outside its own bay. Every existing button keeps doing exactly what it always did.
- **Goes back one level at a time**: STBY from a bay's content returns to the hub; STBY from the hub returns to the host's home.
- **An open contract for adding a new bay**: see [HOSTING.md](GameData/MFDExtension/HOSTING.md) if you want to host your own mod's screen here, or bridge another mod's orphaned RPM-style display the way NavInstruments' is.

## Supported mods

| Bay | Mod | Status |
|---|---|---|
| A | [Situational Awareness](https://github.com/Rjoande/SituationalAwareness) | hello-world (real design not built yet) |
| B | [Real Battery](https://github.com/Rjoande/RealBattery) | working — real per-vessel telemetry |
| C | [KRAB-9000](https://github.com/Rjoande/KRAB) | hello-world (real design not built yet) |
| D | [KRILL](https://github.com/Rjoande/KRILL) | hello-world (real design not built yet) |
| E | [NavInstruments](https://github.com/net-lisias-kspu/NavInstruments/releases)* | working |
| F | CAS (built in) | working† |

> *bridges its own HSI/ILS display, rescued from patches that go silently dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent
> †a textual WARNING/CAUTION/ADVISORY fault summary, self-contained (not a hosted mod's bay) — reads DangIt, FAR, and RealBattery (runaway/overheat/end-of-life only, not charge level — that's still Real Battery's own bay B) by reflection if any is installed, otherwise shows a "no fault sources" message. Scrolls with the monitor's own Λ/V/O keys; **x** mutes DangIt's aural alarm from the cockpit without clearing anything from the screen.

## Extras (optional)

Two custom colour modes for [VesselView Continued](https://github.com/linuxgurugamer/VesselView), installed alongside it without patching any of its files. Each ships as its own DLL under `Extras/` — delete either folder and nothing else changes. Both are unrelated to the MFD bays above and require VesselView; the rest of the framework does not.

| Mode | What it shows |
|---|---|
| **EFIS SEVERITY** | Every part on the same WARNING/CAUTION/ADVISORY severity scale CAS uses, replacing VesselView's own STATE-mode colours. A pulsing red border marks genuine malfunctions, separately from mere depletion (an empty tank, a flamed-out engine), which get colour only. |
| **SHELL TEMP** | Every part by skin temperature, on a continuous blue→cyan→green→yellow→red heat map that accelerates into the danger band above 60%/80% of that part's own skin limit. |

Both offer a wireframe toggle, switchable in flight from their own submenu without leaving the 3D view.

## Requirements

- Kerbal Space Program 1.12.5
- [ModuleManager](https://github.com/sarbian/ModuleManager)
- [MOARdV's Avionics Systems](https://github.com/MOARdV/AvionicsSystems). This release targets the MAS-flavoured BasicMFD prop (`MAS_JSI_BasicMFD`); it does nothing on an install without MAS. Note that MAS itself commonly promotes plain RasterPropMonitor screens to this same MAS prop across an entire install, so "I only use RPM, not MAS" installs are less common than they look. Check for `MOARdV/Patches/000_JSI-To-MAS.cfg` if unsure which one your IVAs actually use.
- The CAS bay (F) requires the compiled `Plugins/MFDExtension.dll` shipped with this release — the rest of the framework is still pure config/Lua and works without it, but F specifically won't if the DLL is missing.
- DangIt, Ferram Aerospace Research (FAR), and/or Real Battery are all optional — CAS reads whichever are present by reflection, and says so plainly if none are installed.

## Installation

Copy the contents of this repository into your `GameData` folder, so you end up with `GameData/MFDExtension/...`.

## Known limitations & Future Plans

- SA/KRAB/KRILL bays are placeholders (real content isn't built yet). Each will ship from its own mod's repository, following the contract in `HOSTING.md`.
- Requires Avionics Systems; RPM-only installs (no MAS at all) aren't supported by this release. An earlier RPM-only implementation exists, unverified, in the dev repo's `_deprecated/rpm-only/` folder, not part of what's installed from this package.
- Only one host prop (`MAS_JSI_BasicMFD`) is supported for now; extending to other MFD props (ALCOR, StarshipMFD...) is a future step.
- CAS's fault-name abbreviations are keyed on the English strings, so a localized DangIt install falls back to ellipsis truncation for the longer ones.
- English only for now.

Planned next:

- A bay dedicated to [SystemHeat](https://github.com/post-kerbin-mining-corporation/SystemHeat), and one to [DynamicBatteryStorage](https://github.com/post-kerbin-mining-corporation/DynamicBatteryStorage).
- Support for other monitor types beyond `MAS_JSI_BasicMFD`.

## License

[MIT](LICENSE).

## Credits

Author: Rjoande. Built with the help of Claude Code.
