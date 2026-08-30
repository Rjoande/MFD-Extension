# Changelog

## v0.2.0

- **New bay F: CAS (Crew Alerting System)**, a self-contained WARNING/CAUTION/ADVISORY fault summary. Reads DangIt failures (labeled by the specific fault name when known, e.g. "ALTERNATOR", falling back to the generic "FAILURE"), FAR stall warnings ("STALL"), and RealBattery malfunction states ("RUNAWAY"/"OVERHEAT"/"EOL"; end-of-life only, not charge level, which stays on bay B). All by reflection, no compile-time dependency on any of the three optional mods. Shows a "no fault sources detected" message if none are installed.
- **Bay labels renamed to match function, not mod name**, same convention the host prop's own labels (`AUTO`, `GRAPH`, `TRGT`) already use: KRAB-9000 → **FADEC** (bay C), KRILL → **SWC** (bay D), RealBattery → **BMS** (bay B, Battery Management System). Mod names are unchanged everywhere else (page names, `NEEDS[]` tokens, repo links).

### Extras/VVEFIS (optional — requires VesselView Continued)

- **New**: "EFIS SEVERITY", a custom VesselView color mode installed alongside VesselView/VesselViewRPM without patching any of its files (`Extras/VVEFIS/`, its own `VVEFIS.dll` — doesn't affect the rest of the framework or require VesselView for anything else in this package). Colors every part on a WARNING/CAUTION/ADVISORY/INDICATION severity scale grounded in FAA/EASA/MIL-STD color conventions, the same palette CAS uses on its text page. Reads DangIt failures, FAR stall warnings, RealBattery (runaway, overheat, end-of-life, and charge level), and generic fuel/engine condition, replacing VesselView's own separate STATE-mode fill colors and hardcoded engine icons with one coherent per-part reading. A pulsing red border marks genuine malfunctions (DangIt, FAR stall, RealBattery runaway/overheat, active fuel/power/air deprivation) separately from mere depletion (an empty tank, a low or disabled battery, a flamed-out engine), which get color-only feedback (same distinction as a real Master Caution/Warning annunciator).

## v0.1.1

- **All fifteen buttons (A-G, R1-R7) now behave uniformly**, not just A-E: every one redirects into our own world via the same Lua mechanism, with native host behavior preserved outside it. F, G, and the bottom row (NAV/ORB/DOCK/DATA/CREW/RSRC/EXT) — previously unmapped and prone to dropping the player into the host's own pages with no easy way back. Now they show a shared "unassigned module slot" placeholder instead, staying inside the framework's own navigation.
- **Real bay content now live for all four first-party bays** (SA, BATT, KRAB, KRILL), alongside ILS, each one hosted from its own mod's repository per the hosting contract, no longer a bare placeholder for installs that have the mod.
- **Hosting contract revised**: a bay's `MAS_PAGE` and its `MASMonitor` page registration must now ship together, from the hosting mod's own repository — fixes two crash modes found this round (two same-named pages from different repos crash MAS's script loader game-wide; a page name registered with nothing behind it black-screens the whole monitor). See `HOSTING.md` for hosted mods that need to update.

## v0.1.0

- **Hub page** (`MFDExt_Stby`), reachable via NEXT or PREV (symmetric, either button works) from the host's own standby page.
- **Level-by-level STBY navigation**: a bay's content returns to the hub, the hub returns to the host's home (never skips a level).
- **Five bay slots (A-E)**: SA, BATT, KRAB and KRILL reserved with a two-tier "module not detected" / "detected, awaiting firmware" placeholder each; ILS bridges NavInstruments' own HSI/ILS display via MAS's `RPM_MODULE`, rescuing it from patches that go dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent.
- **Public hosting contract** (`HOSTING.md`) documenting the navigation model, the button-wiring quirks of this host prop, and how to add a new bay or bridge another mod's orphaned RPM-style handler.
