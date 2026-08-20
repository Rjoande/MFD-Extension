# Changelog

## v0.1.1

- **All fifteen buttons (A-G, R1-R7) now behave uniformly**, not just A-E: every one redirects into our own world via the same Lua mechanism, with native host behavior preserved outside it. F, G, and the bottom row (NAV/ORB/DOCK/DATA/CREW/RSRC/EXT) — previously unmapped and prone to dropping the player into the host's own pages with no easy way back. Now they show a shared "unassigned module slot" placeholder instead, staying inside the framework's own navigation.
- **Real bay content now live for all four first-party bays** (SA, BATT, KRAB, KRILL), alongside ILS, each one hosted from its own mod's repository per the hosting contract, no longer a bare placeholder for installs that have the mod.
- **Hosting contract revised**: a bay's `MAS_PAGE` and its `MASMonitor` page registration must now ship together, from the hosting mod's own repository — fixes two crash modes found this round (two same-named pages from different repos crash MAS's script loader game-wide; a page name registered with nothing behind it black-screens the whole monitor). See `HOSTING.md` for hosted mods that need to update.

## v0.1.0

- **Hub page** (`MFDExt_Stby`), reachable via NEXT or PREV (symmetric, either button works) from the host's own standby page.
- **Level-by-level STBY navigation**: a bay's content returns to the hub, the hub returns to the host's home (never skips a level).
- **Five bay slots (A-E)**: SA, BATT, KRAB and KRILL reserved with a two-tier "module not detected" / "detected, awaiting firmware" placeholder each; ILS bridges NavInstruments' own HSI/ILS display via MAS's `RPM_MODULE`, rescuing it from patches that go dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent.
- **Public hosting contract** (`HOSTING.md`) documenting the navigation model, the button-wiring quirks of this host prop, and how to add a new bay or bridge another mod's orphaned RPM-style handler.
