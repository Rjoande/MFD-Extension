# Changelog

## v0.1.0

- **Hub page** (`MFDExt_Stby`), reachable via NEXT or PREV (symmetric, either button works) from the host's own standby page.
- **Level-by-level STBY navigation**: a bay's content returns to the hub, the hub returns to the host's home (never skips a level).
- **Five bay slots (A-E)**: SA, BATT, KRAB and KRILL reserved with a two-tier "module not detected" / "detected, awaiting firmware" placeholder each; ILS bridges NavInstruments' own HSI/ILS display via MAS's `RPM_MODULE`, rescuing it from patches that go dead on any install where Avionics Systems promotes RasterPropMonitor screens to its own equivalent.
- **Public hosting contract** (`HOSTING.md`) documenting the navigation model, the button-wiring quirks of this host prop, and how to add a new bay or bridge another mod's orphaned RPM-style handler.
