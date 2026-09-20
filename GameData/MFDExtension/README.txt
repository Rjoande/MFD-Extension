MFD EXTENDED  v0.4.1
====================

An additive "second world" for an existing RasterPropMonitor / MOARdV's
Avionics Systems (MAS) multi-function display, reachable with a button press,
exitable the same way, without replacing, renaming, or otherwise disturbing
anything the host IVA already does.


WHAT IT DOES
------------

* A shared module bay inside an existing MFD, not a new prop of its own:
  from the host's own home page, press NEXT or PREV (either one works) to
  enter.
* Never touches the host's own pages or buttons outside its own bay. Every
  existing button keeps doing exactly what it always did.
* Goes back one level at a time: STBY from a bay's content returns to the
  hub; STBY from the hub returns to the host's home.
* An open contract for adding a new bay: see HOSTING.md (this folder) if you
  want to host your own mod's screen here, or bridge another mod's orphaned
  RPM-style display the way NavInstruments' is.


SUPPORTED MODS
--------------

Top row (A-G) is flight and command, bottom row (R1-R7) is vessel systems, on
the EICAS/ECAM model.

  Bay  Label  Mod / content                          Status
  ---  -----  -------------------------------------  -----------
  A    SA     Situational Awareness                  hello-world
  B    ILS    NavInstruments                         working
  C    FADEC  KRAB-9000                              hello-world
  D    SWC    KRILL                                  hello-world
  R1   CAS    Crew Alerting System (built in)        working
  R2   BMS    Real Battery                           working
  R3   ELEC   Electrical ledger (built in),          working
              reads DynamicBatteryStorage
  R4   TCS    Thermal Control System (built in),     working
              reads SystemHeat

"hello-world" = the bay is wired and answers, but its real content isn't
built yet; it will ship from that mod's own repository.

  Situational Awareness   https://github.com/Rjoande/SituationalAwareness
  NavInstruments          https://github.com/net-lisias-kspu/NavInstruments
  KRAB-9000               https://github.com/Rjoande/KRAB
  KRILL                   https://github.com/Rjoande/KRILL
  Real Battery            https://github.com/Rjoande/RealBattery
  DynamicBatteryStorage   https://github.com/post-kerbin-mining-corporation/
                          DynamicBatteryStorage
  SystemHeat              https://github.com/post-kerbin-mining-corporation/
                          SystemHeat

NavInstruments (B)
  Bridges its own HSI/ILS display, rescued from patches that go silently dead
  on any install where Avionics Systems promotes RasterPropMonitor screens to
  its own equivalent.

Crew Alerting System, CAS (R1)
  A textual WARNING/CAUTION/ADVISORY fault summary, self-contained (not a
  hosted mod's bay). Reads DangIt, FAR, RealBattery (runaway/overheat/end-of-
  life) and SystemHeat (loop overtemp, reactor core, SCRAM, meltdown, core
  damage, boil-off) if any is installed, otherwise shows a "no fault sources"
  message. Scrolls with the monitor's own UP/DOWN/HOME keys; the red "x" key
  mutes DangIt's aural alarm from the cockpit without clearing anything from
  the screen.

Electrical, ELEC (R3)
  The vessel's electrical ledger on three pages (press R3 to cycle, or step
  with NEXT/PREV): a summary (net flow, generated/consumed, EC level, timewarp
  buffer, top loads and sources), then every source and every load grouped by
  category with subtotals, scrollable. ENTER (the green arrow) switches
  between PLANT (batteries kept out of the totals and shown on their own
  STORAGE row; with Real Battery installed DynamicBatteryStorage's own net
  figure sits near zero, because storage always absorbs or supplies the
  balance) and TOTAL (DynamicBatteryStorage's own numbers).

Thermal Control System, TCS (R4)
  The vessel's thermal picture on three pages (press R4 to cycle, or step
  with NEXT/PREV): a summary (heat generated/rejected, one row per heat loop
  with temperature vs nominal, net flux and a NOMINAL/HEATING/OVERTEMP/
  CRITICAL status on SystemHeat's own thresholds, reactor/cryo-tank/heat-sink
  counts), then every loop with its members (what each source adds, what the
  loop actually allocated to each radiator or sink), then every reactor
  (state, power, heat, core temperature, throttle, core integrity, fuel life).
  Fusion reactors from Far Future Technologies are listed too, with what
  SystemHeat's own panel reads from them. Says so plainly if SystemHeat isn't
  installed.


EXTRAS (OPTIONAL)
-----------------

Three custom colour modes for VesselView Continued
(https://github.com/linuxgurugamer/VesselView), installed alongside it. Each
ships as its own DLL under Extras/ (delete any folder you don't want and
nothing else changes). All three are unrelated to the MFD bays above and
require VesselView; the rest of the framework does not.

  EFIS SEVERITY  Every part on the same WARNING/CAUTION/ADVISORY severity
                 scale CAS uses, replacing VesselView's own STATE-mode
                 colours. A pulsing red border marks genuine malfunctions,
                 separately from mere depletion (an empty tank, a flamed-out
                 engine), which get colour only.

  SHELL TEMP     Every part by skin temperature, on a continuous 5-colour
                 heat map (orange starts in the danger band above 60%/80% of
                 that part's own skin limit).

  HOLO           A sci-fi holographic look instead of a diagnostic readout:
                 parts are cobalt blue with a light-blue wireframe by
                 default. Only WARNING/CAUTION parts get distinct treatment
                 (fill and wireframe turn red, the outline breathes);
                 everything else stays visually nominal.

EFIS SEVERITY and SHELL TEMP offer a wireframe toggle, switchable in flight
from their own submenu.


REQUIREMENTS
------------

* Kerbal Space Program 1.12.5
* ModuleManager            https://github.com/sarbian/ModuleManager
* MOARdV's Avionics Systems https://github.com/MOARdV/AvionicsSystems
  This release targets the MAS-flavoured BasicMFD prop (MAS_JSI_BasicMFD); it
  does nothing on an install without MAS. Note that MAS itself commonly
  promotes plain RasterPropMonitor screens to this same MAS prop across an
  entire install. Check for MOARdV/Patches/000_JSI-To-MAS.cfg if unsure which
  one your IVAs actually use.
* DangIt, Ferram Aerospace Research (FAR) and/or Real Battery are all
  optional. CAS reads whichever are present. DynamicBatteryStorage and
  SystemHeat are also optional, but they're what the ELEC and TCS bays read.


INSTALLATION
------------

Copy the MFDExtension folder into your GameData folder, so you end up with
GameData/MFDExtension/... . To remove one of the VesselView extras, delete its
folder under GameData/MFDExtension/Extras/.


KNOWN LIMITATIONS & FUTURE PLANS
--------------------------------

* SA/KRAB/KRILL bays are placeholders (real content isn't built yet). Each
  will ship from its own mod's repository, following the contract in
  HOSTING.md.
* Requires Avionics Systems; RPM-only installs (no MAS at all) aren't
  supported by this release. An earlier RPM-only implementation exists,
  unverified, in the source repository's _deprecated/rpm-only/ folder, not
  part of what's installed from this package. Free to use for anyone wanting
  to experiment, but no support is offered on this branch.
* Only one host prop (MAS_JSI_BasicMFD) is supported for now; extending to
  other MFD props (ALCOR, NearFuture, StarshipMFD...) is a future step.
* CAS's fault-name abbreviations are keyed on the English strings, so a
  localized DangIt install falls back to ellipsis truncation for the longer
  ones.
* English only for now.

Planned next:

* Support for other monitor types beyond MAS_JSI_BasicMFD.


LICENSE
-------

MIT, see LICENSE.txt in this folder.


CREDITS
-------

Author: Rjoande. Built with the help of Claude Code.
Source and issues: https://github.com/Rjoande/MFD-Extension
