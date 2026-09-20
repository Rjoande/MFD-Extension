using UnityEngine;
using static MFDExtension.Shared.FuelEngineReader;

namespace MFDExtension.Shared
{
    // One part's severity: a WARNING/CAUTION/ADVISORY/INDICATION tier and the
    // color that renders it. Several signal sources are read independently and
    // the highest tier wins - see GetStatus.
    internal readonly struct PartStatus
    {
        internal readonly Tier Tier;
        internal readonly Color Color;
        internal readonly bool Acknowledged; // only meaningful for Warning/Caution - see VVEFISSeverity.BoxColor

        // Whether a Warning/Caution status also raises the red border alarm.
        // False for "running low / out" readings (fuel, SC_SOC, a flamed-out
        // engine): color only. True for diagnosed malfunctions, hence default.
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
        // Unused on purpose - kept for future reuse, do not remove.
        internal static readonly Color AdvisoryCyan = Color.cyan;
        internal static readonly Color AdvisoryBlue = Color.blue;

        internal static readonly Color IndicationGreen = Color.green;
        internal static readonly Color IndicationNeutral = new Color(0.8f, 0.8f, 0.8f);

        // Border pulse for WARNING/CAUTION: 1 Hz sine, inside the real Master
        // Caution band and calmer than Master Warning's 3-5 Hz.
        private const float BlinkHz = 1f;

        // Every source computes its own best-fit status; the highest tier wins,
        // ties going to the earlier candidate below (a diagnosed failure
        // outranks an inferred resource/engine reading).
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
            // A RealBattery part never falls through to the generic reading:
            // EC and StoredCharge are different quantities on different
            // scales, so averaging them is meaningless. SC_SOC replaces it.
            PartStatus? batteryStatus = battery.Present ? (PartStatus?)GetRealBatteryStatus(battery) : null;
            PartStatus? resourceStatus = battery.Present ? null : GetResourceOrEngineStatus(part);

            // `best` starts null, not at a neutral Indication default: a
            // same-tier candidate can never beat it, so every genuine
            // Indication reading would be discarded in favor of that default.
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

        // MapPriorityToTier maps an unrecognized Priority string (localized
        // install, raw #LOC tag) to Caution rather than dropping the failure.
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

        // Order is deliberate and NOT by tier: SC_SOC is checked last, so a
        // disabled battery reads "disabled" instead of "low charge" - one that
        // isn't even trying to operate shouldn't raise a charge caution.
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

            // Same 3-band tank palette as GetResourceOrEngineStatus: Caution
            // and never Warning, so a merely depleted battery cannot outrank a
            // real fault on the same part; "empty" is red with no border.
            if (battery.SC_SOC < TankEmptyThreshold) return new PartStatus(Tier.Caution, WarningColor, false, alarm: false);
            if (battery.SC_SOC < TankCautionThreshold) return new PartStatus(Tier.Caution, CautionAmber, false, alarm: false);
            return new PartStatus(Tier.Indication, IndicationGreen, false);
        }

        // A part carrying its own resources (a tank, an SRB) is colored by
        // that level rather than by engine condition: a live fraction beats a
        // coarse flag, and an unstaged solid stops reading as "inactive". A
        // part with no local resources falls through to the engine condition.
        //
        // Known and accepted: a modded engine with a small integrated tank AND
        // crossfeed can read near-empty while running fine off the remote supply.
        private static PartStatus? GetResourceOrEngineStatus(Part part)
        {
            if (TryGetFuelFraction(part, out float fuelFraction))
            {
                // Both bands are Caution, never Warning: Warning is reserved
                // for diagnosed malfunctions, and an empty tank is an expected
                // end state that must not outrank a real fault on the same
                // part. "Empty" reuses the alarm red, told apart by the
                // missing border (alarm:false), not by a different hue.
                if (fuelFraction < TankEmptyThreshold) return new PartStatus(Tier.Caution, WarningColor, false, alarm: false);
                if (fuelFraction < TankCautionThreshold) return new PartStatus(Tier.Caution, CautionAmber, false, alarm: false);
                return new PartStatus(Tier.Indication, IndicationGreen, false);
            }

            // Being deprived of a propellant right now is a live malfunction,
            // border alarm included; FlamedOut is a completed event, so yellow
            // without one. Ready (ignited, idling at zero thrust) is the
            // noteworthy state - never-ignited has nothing to report.
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
                    // No engine module, or never ignited: no signal at all.
                    return null;
            }
        }

        internal static Color FillColor(Part part)
        {
            return GetStatus(part).Color;
        }

        internal static Color BoxColor(Part part)
        {
            PartStatus status = GetStatus(part);

            // Nothing to flag: black, invisible against the render texture.
            if (status.Tier != Tier.Warning && status.Tier != Tier.Caution)
            {
                return Color.black;
            }

            // Yellow Caution states opt out of the border - see PartStatus.Alarm.
            if (!status.Alarm)
            {
                return Color.black;
            }

            // The border is the alarm channel: one fixed red for both tiers,
            // like a Master Warning/Caution light, while the fine-grained
            // color coding stays on the fill. Steady once acknowledged.
            if (status.Acknowledged)
            {
                return WarningColor;
            }

            float pulse = (Mathf.Sin(Time.time * 2f * Mathf.PI * BlinkHz) + 1f) * 0.5f;
            return Color.Lerp(Color.black, WarningColor, pulse);
        }
    }
}
