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
| F, G | — | unclaimed — shows the shared "unassigned slot" page, open for a future bay |
| R1-R7 (NAV/ORB/DOCK/DATA/CREW/RSRC/EXT) | — | same shared "unassigned slot" page as F/G |

Order isn't arbitrary but isn't sacred either: A-D were reserved first for
our own four mods when the project started, E was the first slot given to
an external mod (NavInstruments) once the additive branch proved out. F/G
and the bottom row are open for the next ones.

**Unclaimed slots stay inside our world.** Before 2026-08-19, pressing F,
G, or any of the bottom row from inside MFDExt dropped the player straight
into the host's own native pages (Docking, ShipInfo, EngineIgnitor...) —
technically harmless, but STBY from there goes straight to the host's
home (it's a genuine host page, not one of ours), not back to our hub, so
the player had to re-enter through NEXT/PREV to get back. All nine now
redirect to one shared placeholder page (`Pages/MFDExt_Unclaimed.cfg`)
when pressed from inside our world, exactly like a real bay would — their
native behavior from a host page is untouched. Claiming one of these for
a real bay later means giving it its own `MFDExt_Button<X>` target instead
of `MFDExt_Unclaimed`, same recipe as any other bay.

## How the physical buttons actually work (read this before adding a bay)

This is host-specific knowledge, verified by reading `MAS_JSI_BasicMFD.cfg`
directly rather than assumed — a different host prop may wire things
differently, re-verify before reusing this guide as-is elsewhere.

- **Every one of A-G, R1-R7, and STBY is prop-wide, not per-page.**
  Natively, each one's `onClick` is either a single hardcoded
  `fc.SetPersistent("%AUTOID%", "FixedPageName")` (B, C, F, G, STBY, R1,
  R3-R7) or a `fc.SendSoftkey("%AUTOID%", N)` dispatch (A: 9, D: 12, R2:
  17) that looks up whatever the *currently active page* bound to that
  softkey index — but in both cases the collider itself is registered once
  on `MASComponent`, the same for every page, forever. This is the same
  category of constraint as RPM's `globalButtons` (verified independently
  on the RPM side, see the CLAUDE.md hub for that investigation): once a
  physical button is wired this way, no page-local binding can ever
  intercept it on its own.
- **All fifteen are overridden the same way, in Lua.** We patch every
  one's `COLLIDER_EVENT` to call a `MFDExt_Button<X>(monitorID)` function
  (`Config/Additive/MAS_BasicMFD.cfg` §3), and `Scripts/MFDExt.lua`
  centralizes the actual decision in one table (`MFDExt_OwnPages`) and one
  helper (`MFDExt_Redirect(monitorID, ownPage, hostFallback)`) shared by
  all of them. `hostFallback` is a function you supply that replays
  whatever that button natively did — for the softkey-routed ones (A/D/R2)
  that means calling `fc.SendSoftkey(id, N)` from Lua (confirmed callable
  there, same `fc` proxy already used for `GetPersistent`/`SetPersistent`)
  rather than hardcoding a page name, specifically so a host page's own
  softkey binding (which can be dynamic — the host's home page reads a
  per-monitor persistent preference for A/D/R2, not a fixed target) keeps
  working unmodified outside our world.
- Add your bay's page name to `MFDExt_OwnPages` and reuse `MFDExt_Redirect`
  for a new button — don't duplicate the branching logic by hand.

## Adding a new bay

1. **Pick a free slot** (F, G, or any of R1-R7/NAV-ORB-DOCK-DATA-CREW-RSRC-EXT
   today — all currently point to the shared `MFDExt_Unclaimed` placeholder).
2. **Write your `MAS_PAGE`**, gated `NEEDS[!YourAssembly]` — see
   "Not-detected placeholder" below if you don't have real content yet.
   **Never** also add a `NEEDS[YourAssembly]` variant here once your own
   repo is expected to ship the real page under this same name — see that
   same section for why.
3. **Register the page name** in `MASMonitor`'s `page =` list — a
   `MAS_PAGE` that isn't listed there is unreachable no matter how it's
   wired. **This registration ships from YOUR repo, alongside your
   `MAS_PAGE`, under the same `NEEDS` condition:**
   ```
   @PROP[MAS_JSI_BasicMFD]:NEEDS[AvionicsSystems&MFDExtension]:FINAL
   {
       @MODULE[MASMonitor]
       {
           page = MFDExt_YourBay
       }
   }
   ```
   It cannot live in MFD Extended's own files: a name in that list with no
   `MAS_PAGE` behind it doesn't degrade to a dead button — it makes
   `MASMonitor.Start()` throw and the **entire monitor black-screens**,
   host pages included (see Gotchas). Only your repo knows for certain
   that your page exists, so only your repo may register it. MFD Extended
   registers each bay's own "not detected" fallback under the matching
   `NEEDS[!YourAssembly]`, so exactly one of the two registrations is
   active on any install.
4. **Wire the button**: add your page name to `MFDExt_OwnPages` in
   `Scripts/MFDExt.lua`, write a `MFDExt_Button<X>(monitorID)` function
   following the existing ones (check the host's own cfg for what that
   button natively did, so your `hostFallback` closure replays it
   faithfully — don't assume it matches another button), and patch the
   host's `COLLIDER_EVENT` (remember the `?` wildcard if its `name =`
   field contains a space — see Gotchas). If you're claiming a slot that
   currently points at `MFDExt_Unclaimed`, change that one
   `MFDExt_Button<X>` function to target your own page instead — the
   shared placeholder just stops being reachable from that button.

## Overriding your own button

By default, pressing a bay's own button while already on that bay's page
does nothing (`MFDExt_Redirect` just leaves you where you are) — every
*other* MFDExt page, and every host page, always jumps straight to your
bay when that button is pressed. If your bay wants to reuse its own
button for something else while it's already active — cycling your own
sub-pages, the way some MAS/RPM screens already do — register a function
in the shared override table, keyed by your bay's page name:

```lua
-- In your own MAS_LUA script (any file, any name - it shares the same
-- global Lua environment as every other MAS_LUA script on the prop).
MFDExt_OwnButtonOverrides = MFDExt_OwnButtonOverrides or {}

MFDExt_OwnButtonOverrides["MFDExt_YourBay"] = function(monitorID)
	-- e.g. cycle to your own second page:
	fc.SetPersistent(monitorID, "MFDExt_YourBay_Page2")
end
```

The `MFDExt_OwnButtonOverrides = MFDExt_OwnButtonOverrides or {}` line
matters: MAS loads every `MAS_LUA` script on a prop into one shared global
environment, but the order between different mods' scripts isn't
guaranteed — this lazy-init pattern means whichever script runs first
creates the table, and every other script (ours included) just reuses it,
regardless of load order. Skip it and you risk indexing a nil table if
your script happens to run before ours.

No override registered is the common case and is completely fine — the
button will simply do nothing while you're already looking at your own
page, exactly as if it weren't wired at all.

## Not-detected placeholder

Every one of our own four bays ships exactly **one** `MAS_PAGE`, gated by
`NEEDS[!X]` (the underlying mod isn't installed at all):

```
<NAME> MODULE
NOT DETECTED

This bay is unconfigured.
Contact your mod provider
for compatible hardware.
```

Tone: dry corporate hardware-manual humor, matching the shared IVA register
elsewhere in this ecosystem ("Junk Systems Inc...", "powered by MOARdV's
Avionics Systems" — citing the tech stack in-universe is already the house
style here, not a fourth-wall break). See `Pages/MFDExt_SA.cfg` for a
working example.

**There used to be a second, `NEEDS[X]` "detected, awaiting firmware"
variant here too — retired on 2026-08-19, don't bring it back.** The idea
was a friendlier middle state for "mod present, real page not built yet",
but it means our own file and the mod's own eventual repo both declare a
`MAS_PAGE` under the same shared name once that mod ships real content —
and two `MAS_PAGE` nodes resolving to one name don't coexist, they crash
MASLoader outright (see Gotchas). The moment a mod's own repo is expected
to declare an independent page under this name, our file may only ever
have the `NEEDS[!X]` variant — no exceptions, no "just in case" middle
tier. The practical cost: a mod that's installed but hasn't shipped its
patch yet still shows "NOT DETECTED" (technically wrong, but the only safe
option) instead of a more honest "awaiting firmware" message.

This doesn't apply to the `RPM_MODULE` bridging pattern below (e.g. ILS) —
there, only *we* ever declare a `MAS_PAGE` under that name; the bridged
mod (NavInstruments) never declares one of its own, so there's no shared
name for two repos to collide on.

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
- **Two `MAS_PAGE` nodes that resolve to the same `name` do NOT coexist
  peacefully — MASLoader crashes with `ArgumentException: An item with the
  same key has already been added` the moment both are present at once.**
  This happens during MASLoader's *global* init, not per-prop, so it takes
  every MAS prop in the game down with it — every `MASFlightComputer`
  reports "Loaded 0 user scripts", not just this mod's. Confirmed in game
  (2026-08-19) the moment RealBattery's own real `MFDExt_BATT` page shipped
  alongside our still-present `NEEDS[RealBattery]` placeholder variant of
  the same name — see "Not-detected placeholder" above for the corrected,
  single-variant pattern this forced: our own file may only ever carry
  `NEEDS[!X]` for a bay whose real content is expected to ship from
  another repo, never a matching `NEEDS[X]` "just in case" variant.
- **A name in `MASMonitor`'s `page =` list with no `MAS_PAGE` behind it is
  equally fatal, in the opposite direction: `MASMonitor.Start()` throws
  `No MAS_PAGE found for '...'` and the whole monitor fails to configure —
  black screen, host pages included, on every instance of the prop.**
  Confirmed in game (2026-08-19, same session as the collision above): our
  `page =` list registered every bay name unconditionally while the pages
  themselves were `NEEDS`-gated — on an install where a bay's mod was
  present but its page didn't exist yet, the monitor died entirely. Hence
  the rule in "Adding a new bay" step 3: every `page =` registration must
  be gated by exactly the same condition as the page's own existence, and
  a hosted mod's registration ships from the hosted mod's own repo. (A
  *button target* pointing at a nonexistent page, by contrast, is benign —
  MAS just logs "Unable to switch to page" and stays put — so the Lua
  redirects don't need this gating, only the `page =` list does.)

## Status of this release

Only the hub, navigation, and the ILS bay have real functionality. SA,
BATT, and KRAB show the "not detected" placeholder — their actual content
ships from each mod's own repository, following the "adding a new bay"
recipe above, once each one has something to show. KRILL is in the same
position as of 2026-08-19 (its real content moved from living directly in
this repo to shipping from KRILL's own, per the same recipe). F, G, and
the bottom row are unclaimed, showing the shared "unassigned slot" page
rather than leaking into the host's own ecosystem.
