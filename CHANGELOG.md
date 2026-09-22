# Changelog

## v0.4.2

- **Compact lists on TCS LOOPS and ELEC.** Identical parts (a symmetry group of radiators, a bank of solar panels) fold into one row, with their heat or power summed. Use MFD's buttons ◄/► to collapse/expand.

## v0.4.1

- **Fixed: black screen on installs without RealBattery.** The BMS bay's fallback page was renamed in v0.3.1 but its registration with the host monitor was not, so the monitor was told about a page that did not exist and failed to configure, taking every page of every BasicMFD in the IVA down with it. Installs with RealBattery were never affected.

## v0.4.0

- **New bay R3: ELEC**, the vessel's electrical ledger read from DynamicBatteryStorage, on three pages.
- **New bay R4: TCS** (Thermal Control System), the vessel's thermal picture read from SystemHeat, including loop details and nuclear reactors dedicated page.
- **CAS reads SystemHeat**: heat loop above nominal (`OVHT`, CAUTION; WARNING from 500 K over), fission reactor core above nominal (`CORE`, CAUTION; WARNING above critical), inferred `SCRAM` and `MELTDWN` (WARNING), damaged core (`INTEG`) and cryo tank boil-off (`BOILOFF`) (ADVISORY).
- **Bays reordered thematically** (EICAS/ECAM model): top row = flight and command, bottom row = vessel systems.
- **NEXT/PREV step through a bay's pages** (ELEC and TCS; plain per-page softkeys any hosted bay can copy).

## v0.3.1

- **CAS key legend now reads `▲▼: scroll  ○: home`** (was `ΛV`). Its scroll/collapse logic moved into a small engine shared by every text bay this package ships.

### Extras/VVHolo (optional; requires VesselView Continued)

- **New: "HOLO"**, a third VesselView color mode, a sci-fi holographic look (cobalt fill, light-blue wireframe) with WARNING/CAUTION readout.

## v0.3.0

- **CAS expanded:** alerts are ordered by severity (WARNING, then CAUTION, then ADVISORY), so the most urgent ones are always what you see without touching anything, and the page scrolls with the monitor's own **Λ / V** keys (**O** jumps back to the top). When a tier doesn't fit marks the rest as `+N MORE` or collapse to a single preview line .
- **Mute DangIt's alarm from the cockpit**, with the monitor's own **x** key while CAS is up.
- **Long part titles scroll** instead of being cut off with an ellipsis. Fault names sit in a fixed-width column, and the longer ones use standard aeronautical abbreviations (`OVHT`, `RWA`, `CTL SRF`).
- **The hub page reports its own version** at runtime instead of carrying a hardcoded string that silently went stale (it had been reading v0.1.0 through two releases).
- **Two navigation fixes**: a hosted bay's *second* page is now recognized as part of the framework (RealBattery's fleet view was dropping you into MAS's own GRAPH page instead of back to its main screen), and `HOSTING.md` documents the registration step that caused it, so the next bay with a second page doesn't repeat it.

### Extras/VVEFIS (optional; requires VesselView Continued)

- **Wireframe toggle, switchable in flight** from the mode's own submenu, without leaving the live 3D view. Off by default (unchanged appearance); on, part edges are drawn in a darker shade of that same part's severity color, so the outline reinforces the reading instead of competing with it.

### Extras/VVThermalMap (new, optional; requires VesselView Continued)

- **New: "SHELL TEMP"**, a second custom VesselView color mode, independent of EFIS SEVERITY. Colors every part by skin temperature on a continuous 5-colour heat map. Reads the part's internal temperature too in its own danger band, and only if it's doing worse than the skin.

## v0.2.0

- **New bay F: CAS (Crew Alerting System)**, a self-contained WARNING/CAUTION/ADVISORY fault summary. Reads DangIt failures (labeled by the specific fault name when known, e.g. "ALTERNATOR"), FAR stall warnings ("STALL"), and RealBattery malfunction states ("RUNAWAY"/"OVERHEAT"/"EOL"). Shows a "no fault sources detected" message if none are installed.
- **Bay labels renamed to match function, not mod name**, same convention the host prop's own labels (`AUTO`, `GRAPH`, `TRGT`) already use: KRAB-9000 = **FADEC** (bay C), KRILL = **SWC** (bay D), RealBattery = **BMS** (bay B, Battery Management System).

### Extras/VVEFIS (optional; requires VesselView Continued)

- **New**: "EFIS SEVERITY", a custom VesselView color mode installed alongside VesselView/VesselViewRPM. Colors every part on a WARNING/CAUTION/ADVISORY/INDICATION severity scale grounded in FAA/EASA/MIL-STD color conventions, the same palette CAS uses on its text page. Reads DangIt failures, FAR stall warnings, RealBattery (runaway, overheat, end-of-life, and charge level), and generic fuel/engine condition, replacing VesselView's own separate STATE-mode fill colors and hardcoded engine icons with one coherent per-part reading. A pulsing red border marks malfunctions.

## v0.1.1

- **All fifteen buttons (A-G, R1-R7) now behave uniformly**. Every one redirects into our own world via the same Lua mechanism.
- **Real bay content now live for all four first-party bays** (SA, BATT, KRAB, KRILL), alongside ILS, each one hosted from its own mod's repository per the hosting contract.
- **Hosting contract revised**: a bay's `MAS_PAGE` and its `MASMonitor` page registration must now ship together, from the hosting mod's own repository. Fixes two crash modes found this round (two same-named pages from different repos crash MAS's script loader game-wide; a page name registered with nothing behind it black-screens the whole monitor). See `HOSTING.md` for hosted mods that need to update.

## v0.1.0

- **Hub page** (`MFDExt_Stby`), reachable via NEXT or PREV (symmetric, either button works) from the host's own standby page.
- **Level-by-level STBY navigation**: a bay's content returns to the hub, the hub returns to the host's home (never skips a level).
- **Five bay slots (A-E)**: SA, BATT, KRAB and KRILL reserved with a two-tier "module not detected" / "detected, awaiting firmware" placeholder each; ILS bridges NavInstruments' own HSI/ILS display via MAS's `RPM_MODULE`, rescuing it from patches that go dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent.
- **Public hosting contract** (`HOSTING.md`) documenting the navigation model, the button-wiring quirks of this host prop, and how to add a new bay or bridge another mod's orphaned RPM-style handler.
