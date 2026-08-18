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
| A | [Situational Awareness](https://github.com/Rjoande/SituationalAwareness) | placeholder (real content not built yet) |
| B | [Real Battery](https://github.com/Rjoande/RealBattery) | placeholder (real content not built yet) |
| C | [KRAB-9000](https://github.com/Rjoande/KRAB) | placeholder (real content not built yet) |
| D | [KRILL](https://github.com/Rjoande/KRILL) | placeholder (real content not built yet) |
| E | [NavInstruments](https://github.com/net-lisias-kspu/NavInstruments/releases)* | working |

> *bridges its own HSI/ILS display, rescued from patches that go silently dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent

## Requirements

- Kerbal Space Program 1.12.5
- [ModuleManager](https://github.com/sarbian/ModuleManager)
- [MOARdV's Avionics Systems](https://github.com/MOARdV/AvionicsSystems). This release targets the MAS-flavoured BasicMFD prop (`MAS_JSI_BasicMFD`); it does nothing on an install without MAS. Note that MAS itself commonly promotes plain RasterPropMonitor screens to this same MAS prop across an entire install, so "I only use RPM, not MAS" installs are less common than they look. Check for `MOARdV/Patches/000_JSI-To-MAS.cfg` if unsure which one your IVAs actually use.

## Installation

Copy the contents of this repository into your `GameData` folder, so you end up with `GameData/MFDExtension/...`.

## Known limitations & Future Plans

- SA/BATT/KRAB/KRILL bays are placeholders (real content isn't built yet). Each will ship from its own mod's repository, following the contract in `HOSTING.md`.
- Requires Avionics Systems; RPM-only installs (no MAS at all) aren't supported by this release. An earlier RPM-only implementation exists, unverified, in the dev repo's `_deprecated/rpm-only/` folder, not part of what's installed from this package.
- Only one host prop (`MAS_JSI_BasicMFD`) is supported for now; extending to other MFD props (ALCOR, StarshipMFD...) is a future step.
- English only for now.

## License

[MIT](LICENSE).

## Credits

Author: Rjoande. Built with the help of Claude Code.
