# MFD Extended — hosting guide

MFD Extended adds a second, additive "world" to an existing RasterPropMonitor
(RPM) / Avionics Systems (MAS) multi-function display, reachable with a
button press and exitable the same way — without replacing, renaming, or
otherwise disturbing anything the host IVA already does. This document is
the contract for anyone adding a module (a "bay") to that world: our own
mods, or any third-party mod that wants a home for its own screens.

Everything here is the MAS side of the additive branch. An equivalent RPM
implementation exists but is unverified and archived — see
`_deprecated/rpm-only/` in the dev repo, not part of this payload.

## Navigation model

```
   host's own home page                  MFD Extended hub (MFDExt_Stby)
  ┌───────────────────────┐  NEXT/PREV  ┌───────────────────────────┐
  │ ATT GRAPH TRGT AUTO...│ ──────────► │ SA BATT KRAB KRILL ILS ...│
  │                        │ ◄────────── │                           │
  └───────────────────────┘   STBY      └───────────────────────────┘
                                              │  A-G           ▲
                                              ▼                │  STBY
                                        ┌──────────────┐       │
                                        │  bay content │───────┘
                                        └──────────────┘
```

- **Entry**: from the host's own standby/home page, softkey 7 (buttonR9,
  labelled NEXT) **or** softkey 8 (buttonR10, labelled PREV) both jump to
  `MFDExt_Stby`, our hub. Either button works — deliberately symmetric.
- **Exit**: STBY goes back **one level at a time** — from a bay's content
  page to the hub, from the hub to the host's own home. It never skips a
  level.
- **A-G on the hub and on bay pages**: dedicated to our own bays. Pressing
  the same physical button from a **host** page keeps doing whatever the
  host already had it do — we never touch that behavior.

## Current bay assignment

| Button | Bay | Status |
|---|---|---|
| A | SA (SituationalAwareness) | placeholder ("no monitor yet") |
| B | BATT (RealBattery) | placeholder ("no monitor yet") |
| C | KRAB (KRAB-9000) | placeholder ("no monitor yet") |
| D | KRILL | placeholder ("no monitor yet") |
| E | ILS (NavInstruments) | bridged via `RPM_MODULE`, least battle-tested part of this release |
| F, G | — | free |

Order isn't arbitrary but isn't sacred either: A-D were reserved first for
our own four mods when the project started, E was the first slot given to
an external mod (NavInstruments) once the additive branch proved out. F/G
are open for the next one.

## How the physical buttons actually work (read this before adding a bay)

This is host-specific knowledge, verified by reading `MAS_JSI_BasicMFD.cfg`
directly rather than assumed — a different host prop may wire things
differently, re-verify before reusing this guide as-is elsewhere.

- **A and D are per-page softkeys.** Their `onClick` sends `fc.SendSoftkey`
  (index 9 and 12), and each `MAS_PAGE` decides what that index means for
  itself via a plain `softkey = 9, <expression>` line. No Lua needed — see
  how A and D are bound on `MFDExt_Stby` in `Pages/MFDExt_Stby.cfg`.
- **B, C, E, and STBY are prop-wide, not per-page.** Their `onClick` is a
  single hardcoded `fc.SetPersistent("%AUTOID%", "FixedPageName")`,
  registered once on `MASComponent` — the same for every page, forever.
  This is the same category of constraint as RPM's `globalButtons`
  (verified independently on the RPM side, see the CLAUDE.md hub for that
  investigation): once a physical button is wired this way, no page-local
  binding can ever intercept it.
- **To make a prop-wide button behave differently depending on context**
  (host page vs. one of ours), the decision has to live in Lua: read the
  currently active page with `fc.GetPersistent(monitorID)`, and branch.
  `Scripts/MFDExt.lua` centralizes this in one table
  (`MFDExt_OwnPages`) and one helper (`MFDExt_Redirect`) — add your bay's
  page name to that table and reuse the helper, don't duplicate the
  pattern by hand for every new button.

## Adding a new bay

1. **Pick a free slot** (F or G today) and check on the host's own cfg
   whether that button is softkey-routed or prop-wide (see above) — this
   decides which of the two techniques below you need.
2. **Write your `MAS_PAGE`**, gated for two states — see "Two-tier
   placeholders" below if you don't have real content yet.
3. **Register the page name** in `MASMonitor`'s `page =` list
   (`Config/Additive/MAS_BasicMFD.cfg`) — a `MAS_PAGE` that isn't listed
   there is unreachable no matter how it's wired.
4. **Wire the button**:
   - Softkey-routed (A/D-style): add `softkey = N, fc.SetPersistent(...)`
     directly on `MFDExt_Stby`.
   - Prop-wide (B/C/E/STBY-style): add your page name to
     `MFDExt_OwnPages` in `Scripts/MFDExt.lua`, write a
     `MFDExt_Button<X>(monitorID)` function following the existing ones,
     and patch the host's `COLLIDER_EVENT` (remember the `?` wildcard if
     its `name =` field contains a space — see Gotchas).

## Two-tier placeholders

Every one of our own four bays ships two mutually-exclusive `MAS_PAGE`
declarations sharing one name, gated by `NEEDS[X]` / `NEEDS[!X]` — only one
ever loads, so there's no naming collision:

- **"Module not detected"** — the underlying mod isn't installed at all.
  Tone: dry corporate hardware-manual humor, matching the shared IVA
  register elsewhere in this ecosystem ("Junk Systems Inc...", "powered by
  MOARdV's Avionics Systems" — citing the tech stack in-universe is already
  the house style here, not a fourth-wall break).
  ```
  <NAME> MODULE
  NOT DETECTED

  This bay is unconfigured.
  Contact your mod provider
  for compatible hardware.
  ```
- **"Detected, awaiting firmware"** — the mod IS installed, but its real
  MFD Extended page hasn't been built yet.
  ```
  <NAME> MODULE
  DETECTED

  Awaiting firmware install.
  Check for a bay driver update
  from your mod provider.
  ```

See `Pages/MFDExt_SA.cfg` for a working example of both variants together.

## Bridging a "parasitic" mod (RPM_MODULE)

Some mods (NavInstruments is our first case) have real, working RPM-style
handler code but no monitor of their own — they patch INTO other mods' MFD
props. On an install where AvionicsSystems promotes every RPM monitor to
MAS (see Gotchas), those patches are RPM-only and silently dead. Bridging
the mod's existing handler into one of our own bays, via MAS's
`RPM_MODULE` node, rescues working functionality instead of leaving it
orphaned:

```
MAS_PAGE:NEEDS[TheirModFolder]
{
	name = MFDExt_YourBay

	RPM_MODULE
	{
		moduleName = TheirHandlerClass
		renderMethod = TheirRenderMethod
		buttonClickMethod = TheirClickMethod   // optional
		renderSize = 640, 640                  // needed if the handler assumes a 640x640 texture
	}
}
```

**Unlike RPM's own PAGEHANDLER/BACKGROUNDHANDLER, MAS does not
auto-instantiate the bridged class** — you must also add it yourself as a
plain sibling `MODULE`, carrying whatever config fields the class expects
(copy them from the source mod's own RPM patch):

```
@PROP[YourHostProp]:NEEDS[AvionicsSystems&TheirModFolder]:FINAL
{
	MODULE
	{
		name = TheirHandlerClass
		// ...whatever fields their own RPM BACKGROUNDHANDLER/PAGEHANDLER block had
	}
}
```

See `Config/Additive/MAS_BasicMFD.cfg` (§4) and `Pages/MFDExt_ILS.cfg` for
the NavInstruments example. This is the least battle-tested pattern in this
release — expect to need a debugging pass for any new mod you bridge this
way.

## Gotchas (learned the hard way, don't repeat)

- **With AvionicsSystems installed, RPM props may not be what the player
  actually sees.** `MOARdV/Patches/000_JSI-To-MAS.cfg` renames every placed
  `RasterPropMonitorBasicMFD` (and others) to its MAS equivalent, in every
  IVA, no guard. Patching the RPM prop definition applies without error but
  has zero effect — always check whether your target prop survives that
  promotion before writing a patch against it.
- **`MAS_LUA` is a top-level node, never nested inside `@PROP[...]`.**
  Nested, it loads without error but is never found — any function inside
  throws `attempt to call a nil value` when called, not a Lua bug.
- **A literal space in an MM selector `[...]` fails to load.** Use `?`
  (MM's single-character wildcard) in its place —
  `@COLLIDER_EVENT[C?button]` matches `name = C button`.
- **`globalButtons` (RPM) and prop-wide `COLLIDER_EVENT`s (MAS) both
  permanently claim a physical button for the whole prop, not per page.**
  No page-local binding can ever override one — the workaround always has
  to live in a handler that inspects current state (a C# `buttonClickMethod`
  reading RPM internals via reflection on the RPM side; a Lua function
  reading `fc.GetPersistent` on the MAS side, which is public and
  documented — prefer the MAS route whenever the target is MAS).

## Status of this release

Only the hub, navigation, and the ILS bay have real functionality. SA,
BATT, KRAB and KRILL are "awaiting firmware" placeholders — their actual
content ships from each mod's own repository, following the "adding a new
bay" recipe above, once each one has something to show.
