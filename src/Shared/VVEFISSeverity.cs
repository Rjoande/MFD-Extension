using UnityEngine;
using static MFDExtension.Shared.FuelEngineReader;

namespace MFDExtension.Shared
{
    // The coherent WARNING/CAUTION/ADVISORY/INDICATION palette designed in
    // chat (2026-08-19) - consolidates VesselView's previously separate,
    // inconsistent visual languages (STATE mode fill color, hardcoded engine
    // icon colors, FUEL/STALL gradients, and now DangIt's own severity) into
    // one per-part severity computed here, instead of reskinning each one in
    // place. Real-world grounding: FAA AC 25-11 / EASA / MIL-STD-1472H color
    // hierarchy, and DangIt's own existing HIGH/MEDIUM/LOW Priority field.
    //
    // MOVED into src/Shared/ on 2026-09-01 (was Extras/VVEFIS/src/, VVEFIS's
    // own namespace) so this severity computation could be source-linked,
    // not duplicated, by a second physically-separate VesselView custom
    // mode ("EFIS WF" - same palette, plus a wireframe overlay) alongside
    // VVEFIS's own "EFIS SEVERITY". Same rationale as the 2026-08-24
    // "shared basket" refactor that consolidated DangIt/FAR/RealBattery
    // readers here in the first place: two DLLs computing the same tier/
    // color logic from copy-pasted sources is exactly the kind of drift
    // that refactor was meant to prevent (see DangItBridge.cs's header).
    // EFIS WF itself was archived the same day after an in-game look
    // (_deprecated/VVEFISWF/, user's call - the wireframe didn't read
    // well) - this file stays in Shared/ rather than moving back, on the
    // same "keep the reserved slot" logic already applied once to
    // AdvisoryCyan (see log 63): if a wireframe-style twin (or anything
    // else wanting this exact severity computation) comes back, the
    // source-linking is already in place, nothing to redo.
    //
    // PartStates.DEACTIVATED was deliberately dropped from this palette
    // (2026-08-19): decompiled Part.cs and 6 candidate stock modules
    // (ModuleEngines, ModuleDecouple, ModuleAnchoredDecoupler, ModuleGenerator,
    // ModuleAnimateGeneric, ModuleDeployableSolarPanel) - none of them call
    // Part.deactivate() during normal gameplay. Its only real callers are the
    // save-load state-resume path and pre-destruction cleanup, neither a
    // meaningful "the player should look at this" signal. Not worth a color.
    internal readonly struct PartStatus
    {
        internal readonly Tier Tier;
        internal readonly Color Color;
        internal readonly bool Acknowledged; // only meaningful for Warning/Caution - see VVEFISSeverity.BoxColor

        // Whether this status should trigger the red border alarm when its
        // Tier is Warning/Caution (irrelevant otherwise - BoxColor gates on
        // Tier first). Confirmed with the user (2026-08-24, after the first
        // in-game screenshot): CautionYellow states are all "running low/out"
        // readings (SC_SOC, generic FUEL, a flamed-out engine) - EOL/depletion,
        // not a diagnosed malfunction - so they get color-only feedback, no
        // border. CautionAmber states (DangIt MEDIUM, RealBattery overheat,
        // an engine actually deprived of fuel/power/air right now) are real
        // malfunctions and do get the border, same as Warning. Defaults to
        // true so only the Yellow call sites need to opt out explicitly.
        internal readonly bool Alarm;

        internal PartStatus(Tier tier, Color color, bool acknowledged, bool alarm = true)
        {
            Tier = tier;
            Color = color;
            Acknowledged = acknowledged;
            Alarm = alarm;
        }
    }

    internal static class VVEFISSeverity
    {
        internal static readonly Color WarningColor = new Color(0.75f, 0.15f, 0.15f);

        internal static readonly Color CautionYellow = new Color(1f, 0.9f, 0.1f);
        internal static readonly Color CautionAmber = new Color(1f, 0.75f, 0f);

        internal static readonly Color AdvisoryMagenta = Color.magenta;
        // Unused as of the 2026-08-30 SC_SOC/FUEL 3-band rewrite (was the
        // old gradient's "in use" band) - kept on explicit user request
        // for possible future reuse, not dead code to be cleaned up.
        internal static readonly Color AdvisoryCyan = Color.cyan;
        internal static readonly Color AdvisoryBlue = Color.blue;

        internal static readonly Color IndicationGreen = Color.green;
        internal static readonly Color IndicationNeutral = new Color(0.8f, 0.8f, 0.8f);

        // Border blink rate for WARNING/CAUTION (sine pulse, full cycle per
        // second - inside the real Master Caution band of 1-2 Hz, deliberately
        // calmer than Master Warning's 3-5 Hz). Shared by both tiers for now.
        private const float BlinkHz = 1f;

        // Architecture (2026-08-19): each signal source (DangIt, RealBattery,
        // resource/engine, FAR stall) computes its own best-fit PartStatus
        // independently; whichever reports the highest Tier wins overall
        // (ties broken by the order below - DangIt before the generic
        // resource/engine reading, since a diagnosed failure is more
        // authoritative than an inferred one). The one exception is INSIDE
        // GetRealBatteryStatus itself, where the check order is NOT by tier -
        // see that method for why.
        internal static PartStatus GetStatus(Part part)
        {
            if (part.State == PartStates.DEAD)
            {
                return new PartStatus(Tier.Warning, WarningColor, false);
            }

            PartStatus? stallStatus = null;
            if (FARBridge.TryGetStall(part, out float stall) && stall >= FARBridge.StallWarningThreshold)
            {
                stallStatus = new PartStatus(Tier.Warning, WarningColor, false);
            }

            PartStatus? dangItStatus = GetDangItStatus(part);

            RealBatteryBridge.BatteryInfo battery = RealBatteryBridge.GetInfo(part);
            // A part with a RealBattery module never falls through to the
            // generic resource/engine reading - EC+StoredCharge averaged
            // together would be a meaningless blend of two different
            // physical quantities on different scales (see
            // RealBatteryBridge.cs). SC_SOC replaces it entirely.
            PartStatus? batteryStatus = battery.Present ? (PartStatus?)GetRealBatteryStatus(battery) : null;
            PartStatus? resourceStatus = battery.Present ? null : GetResourceOrEngineStatus(part);

            // best starts NULL, not a hardcoded Indication default (fix
            // 2026-08-26 - "Falla A", found on the Apollo XXI test: a full
            // tank, an actively-thrusting engine, and a full battery are
            // ALL Tier.Indication results, and the old default was itself
            // Tier.Indication - "candidate.Tier > best.Tier" can never be
            // true for a same-tier candidate, so every genuine Indication
            // reading was computed and then silently discarded in favor of
            // the hardcoded neutral gray. Green was unreachable by
            // construction: the S-IVB's full LqdHydrogen tank, the Mainsail
            // under thrust, and a fully-charged RealBattery all read gray
            // in that test for exactly this reason. Now the first non-null
            // candidate wins outright, and only a STRICTLY higher tier
            // overrides it after that - ties still resolve to the earlier
            // candidate in the array, preserving the existing precedence
            // (DangIt over the generic resource/engine reading, etc).
            PartStatus? best = null;
            PartStatus?[] candidates = { stallStatus, dangItStatus, batteryStatus, resourceStatus };
            foreach (PartStatus? candidate in candidates)
            {
                if (!candidate.HasValue) continue;
                if (!best.HasValue || candidate.Value.Tier > best.Value.Tier)
                {
                    best = candidate;
                }
            }
            return best ?? new PartStatus(Tier.Indication, IndicationNeutral, false);
        }

        // Tier comes from the shared MapPriorityToTier - which maps an
        // unrecognized Priority string (localized installs, raw #LOC tags)
        // to Caution instead of dropping it, closing the gap this copy used
        // to have relative to the CAS bay (see DangItBridge.MapPriorityToTier).
        private static PartStatus? GetDangItStatus(Part part)
        {
            if (!DangItBridge.TryGetWorstFailure(part, out DangItBridge.FailureInfo failure, out Tier tier))
            {
                return null;
            }

            switch (tier)
            {
                case Tier.Warning: return new PartStatus(Tier.Warning, WarningColor, failure.Acknowledged);
                case Tier.Caution: return new PartStatus(Tier.Caution, CautionAmber, failure.Acknowledged);
                default: return new PartStatus(Tier.Advisory, AdvisoryMagenta, failure.Acknowledged);
            }
        }

        // Order here is deliberate and NOT "highest tier wins" - confirmed
        // with the user (2026-08-19): SC_SOC has the LOWEST priority of
        // every RealBattery signal, checked last, even though its own
        // resulting tier can be Caution (critically low charge). A disabled
        // battery must show "disabled" (neutral gray, same bucket as an
        // idle/unstaged engine) rather than "low charge" - a battery that
        // isn't even trying to operate showing a caution for its charge
        // level would be misleading noise, not useful information.
        private static PartStatus GetRealBatteryStatus(RealBatteryBridge.BatteryInfo battery)
        {
            if (battery.IsRunaway)
                return new PartStatus(Tier.Warning, WarningColor, false);
            if (battery.Overheating)
                return new PartStatus(Tier.Caution, CautionAmber, false);
            if (battery.BatteryLife < RealBatteryBridge.EolThreshold)
                return new PartStatus(Tier.Advisory, AdvisoryMagenta, false); // shares the shade with DangIt LOW - same "notice, not urgent" bucket
            if (battery.BatteryDisabled)
                return new PartStatus(Tier.Indication, IndicationNeutral, false); // "not doing anything" - same bucket as an idle/unstaged engine

            // Moved onto the same dedicated 3-band tank palette as
            // GetResourceOrEngineStatus (2026-08-30, user's explicit
            // request - restores the parity with generic FUEL that the
            // log 39 design originally called for, this time on the new
            // scheme instead of the old 4-band gradient). Same reasoning
            // applies verbatim: Tier.Caution (not Warning) for both
            // sub-bands so a merely-depleted battery never outranks a
            // real Warning/Caution-level fault on the same part; "empty"
            // reuses WarningColor with alarm:false, same as the tank case.
            if (battery.SC_SOC < TankEmptyThreshold) return new PartStatus(Tier.Caution, WarningColor, false, alarm: false);
            if (battery.SC_SOC < TankCautionThreshold) return new PartStatus(Tier.Caution, CautionAmber, false, alarm: false);
            return new PartStatus(Tier.Indication, IndicationGreen, false);
        }

        // A part carrying its own tracked resources (a fuel tank, or an
        // SRB-style engine with propellant built into the same part) gets
        // colored by that resource level instead of by its generic engine
        // condition - confirmed with the user (2026-08-19): the resource
        // reading is more informative (a live fraction, not a coarse flag)
        // and avoids false cautions like a pristine, not-yet-staged solid
        // upper stage showing "inactive". For a part with no local resources
        // (a typical liquid engine drawing from a separate tank), FUEL has
        // nothing to read and we fall through to the engine condition
        // instead.
        //
        // KNOWN EDGE CASE, accepted, not special-cased: a handful of modded
        // liquid/monoprop engines carry a tiny integrated reserve tank while
        // ALSO crossfeeding from elsewhere - that reserve can read
        // near-empty by this formula while the engine keeps running fine off
        // the remote supply. Affects roughly half a dozen parts across the
        // whole mod ecosystem; not worth the complexity of detecting
        // crossfeed here.
        private static PartStatus? GetResourceOrEngineStatus(Part part)
        {
            if (TryGetFuelFraction(part, out float fuelFraction))
            {
                // Dedicated 3-band tank palette (2026-08-30, user's call
                // after reviewing real-world low-fuel conventions - see
                // FuelEngineReader.TankCautionThreshold/TankEmptyThreshold
                // for the grounding). Replaces the old shared Green/Cyan/
                // Magenta/Yellow FUEL gradient entirely for tanks/
                // resources (SC_SOC below is untouched, still on that
                // gradient - left as an open question, not decided here).
                //
                // Both sub-bands are Tier.Caution, not Warning - confirmed
                // with the user: Warning stays reserved for genuinely
                // diagnosed malfunctions (DangIt HIGH, FAR stall, DEAD).
                // An empty tank is an expected end state, not a fault, so
                // it must never outrank a real Caution-level DangIt
                // failure on the same part in GetStatus's highest-tier
                // arbitration - it would if this were Warning.
                //
                // "Empty" reuses WarningColor's own red rather than a new
                // hex - the user explicitly accepted the visual overlap
                // with a genuine DangIt failure elsewhere on the vessel;
                // alarm:false (no border) is the only differentiator, same
                // mechanism already used for FlamedOut/SC_SOC-low/generic-
                // fuel-low. A part that's simply out of propellant/
                // resources was never meant to look different from one
                // that's failed outright at the fill level, only at the
                // border.
                if (fuelFraction < TankEmptyThreshold) return new PartStatus(Tier.Caution, WarningColor, false, alarm: false);
                if (fuelFraction < TankCautionThreshold) return new PartStatus(Tier.Caution, CautionAmber, false, alarm: false);
                return new PartStatus(Tier.Indication, IndicationGreen, false);
            }

            // NoFuel/NoPower/NoAir promoted to Amber (2026-08-24, user
            // request): the engine is being actively deprived of a
            // propellant it needs RIGHT NOW - a real malfunction condition,
            // not a passive depletion reading - so it gets the border alarm
            // like DangIt/RealBattery malfunctions. FlamedOut stays Yellow:
            // the engine already shut down: a completed event to note, not
            // an ongoing fault demanding the player's eyes.
            //
            // Ready/NotYetActivated SWAPPED (2026-08-27, user's call after
            // seeing both in game): Ready (ignited, holding at zero thrust)
            // now gets AdvisoryBlue - it's live and could start producing
            // thrust at any moment, the more noteworthy of the two. Never-
            // ignited (a solid upper stage still waiting its turn, or any
            // engine simply not yet told to fire) now falls through to the
            // neutral default with no engine module at all - genuinely
            // nothing to report. The original mapping had these backwards.
            switch (GetEngineCondition(part))
            {
                case EngineCondition.NoFuel:
                case EngineCondition.NoPower:
                case EngineCondition.NoAir:
                    return new PartStatus(Tier.Caution, CautionAmber, false);
                case EngineCondition.FlamedOut:
                    return new PartStatus(Tier.Caution, CautionYellow, false, alarm: false);
                case EngineCondition.Ready:
                    return new PartStatus(Tier.Advisory, AdvisoryBlue, false);
                case EngineCondition.Active:
                    return new PartStatus(Tier.Indication, IndicationGreen, false);
                default:
                    // None (no engine module) or NotYetActivated (never
                    // ignited) - no signal, falls through to GetStatus's
                    // neutral default.
                    return null;
            }
        }

        // Root marker REMOVED entirely (2026-08-27, user's call after
        // seeing it in game): the "Falla B" fix (2026-08-26) had already
        // stopped the marker from hiding a genuine Warning/Caution/Advisory
        // on the root part, but a QUIET root part - the common case, e.g. a
        // fully-charged command pod's RealBattery - still showed magenta
        // instead of green, breaking the "nominal = green everywhere" rule
        // the rest of this palette follows. The root part is now just a
        // part like any other: FillColor/BoxColor read straight from
        // GetStatus, no special case. VesselView's own native root/COM
        // marker (a separate overlay, independent of this custom mode's
        // fill delegate) remains available for identifying the root part
        // visually if needed - this palette no longer duplicates it.
        internal static Color FillColor(Part part)
        {
            return GetStatus(part).Color;
        }

        internal static Color BoxColor(Part part)
        {
            PartStatus status = GetStatus(part);

            // Black (opaque, not Color.clear) when there's nothing to flag -
            // reverted 2026-09-01: briefly made transparent to avoid a
            // pixel-level collision with the "EFIS WF" twin mode's wireframe
            // overlay, but WF didn't work out visually and was archived (see
            // _deprecated/VVEFISWF/) - the transparency had no remaining
            // reason to exist once WF was gone, so back to the original
            // black return. Harmless either way for THIS mode on its own:
            // black was already invisible against the black render-texture
            // background, transparency was only ever insurance against a
            // second mode's overlay that no longer ships.
            if (status.Tier != Tier.Warning && status.Tier != Tier.Caution)
            {
                return Color.black;
            }

            // Yellow Caution states (SC_SOC/FUEL/FlamedOut) opt out of the
            // border via Alarm=false - see PartStatus.Alarm and the Amber/
            // Yellow split above. Warning is always alarm=true.
            if (!status.Alarm)
            {
                return Color.black;
            }

            // The border is the alarm channel, separate from the fill's
            // fine-grained color coding (which distinguishes Yellow/Amber
            // sub-levels, DangIt vs. generic readings, etc.). Confirmed
            // with the user (2026-08-24, first in-game test) that the
            // border should always read as a single, unambiguous "look at
            // this part" alarm - always WarningColor red for both
            // WARNING and CAUTION, never the fill's own (possibly
            // yellow/amber) status.Color. Matches a real Master Warning /
            // Master Caution annunciator: the light is a fixed color, the
            // underlying fault's own color coding lives elsewhere.
            if (status.Acknowledged)
            {
                return WarningColor;
            }

            float pulse = (Mathf.Sin(Time.time * 2f * Mathf.PI * BlinkHz) + 1f) * 0.5f;
            return Color.Lerp(Color.black, WarningColor, pulse);
        }
    }
}
