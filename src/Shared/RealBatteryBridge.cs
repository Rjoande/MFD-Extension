using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;

namespace MFDExtension.Shared
{
    // Moved verbatim from Extras/VVEFIS/src/ into the shared basket
    // (2026-08-24 refactor): it was already channel-neutral (no VesselView
    // reference), and living here makes it available to the CAS bay or a
    // future bay B textual monitor at zero cost. Source-linked into both
    // DLLs, see DangItBridge.cs header for the mechanism.
    //
    // RealBattery exposes what we need as plain PUBLIC fields on its own
    // "RealBattery" PartModule (verified against the real source,
    // E:\Giochi\KSP\Mods\RealBattery\RealBatteryRecharged_v3\RealBattery\source\Core\RealBattery.cs,
    // read-only - never modify that repo from here) - no code changes needed
    // there. Read by reflection, same duck-typed pattern as DangItBridge/
    // FARBridge, not a hard reference to RealBattery's own DLL.
    //
    // A part with a RealBattery module carries both ElectricCharge (power)
    // and StoredCharge (energy, fixed 3600:1 ratio to EC) - averaging them
    // the way the generic FUEL fraction does would blend two different
    // physical quantities on different scales into a meaningless number.
    // SC_SOC is RealBattery's own correctly-scaled state-of-charge reading
    // instead, and takes over entirely for these parts - see
    // VVEFISSeverity.GetRealBatteryStatus.
    internal static class RealBatteryBridge
    {
        // RealBattery's own EOL_THRESHOLD constant (verified against its real
        // source, not invented) - the point at which RealBattery itself
        // already considers a cell "end of life" and starts notifying the
        // player. Lives here (2026-08-24) so every channel that reads
        // BatteryLife shares one value instead of a second hand-copied 0.80.
        internal const double EolThreshold = 0.80;

        // "#LOC_RB_Status_Overheat" is the localization key RealBattery itself
        // resolves BatteryChargeStatus to while its (internal, unreadable by
        // us) IsOverheating is true - see RealBattery.cs's own status-update
        // block. Resolved lazily via the same KSP.Localization.Localizer
        // RealBattery uses, not hardcoded English, so this still matches on
        // a localized install. Comparing resolved text is the only public
        // surface for this signal: IsOverheating and its backing flags
        // (OverheatNotified, the private one; ThermalCapFactor, public but
        // ONLY meaningful for InfiniteCycles-chemistry batteries) don't
        // cover both battery chemistries through public members alone -
        // BatteryChargeStatus is the one field RealBattery already unifies
        // both onto (verified on the real source, 2026-08-27, after the
        // first in-game test found overheat silently missing: the OLD
        // `part.temperature > TempOverheat` computation here had no
        // hysteresis and doesn't match RealBattery's own notion of
        // "currently overheating" at all - it was never the right signal).
        private static string overheatStatusText;
        private static string OverheatStatusText => overheatStatusText ?? (overheatStatusText = Localizer.Format("#LOC_RB_Status_Overheat"));

        internal readonly struct BatteryInfo
        {
            internal readonly bool Present;
            internal readonly bool IsRunaway;
            internal readonly bool Overheating; // BatteryChargeStatus == OverheatStatusText - see the comment above OverheatStatusText
            internal readonly double BatteryLife;
            internal readonly bool BatteryDisabled;
            internal readonly double SC_SOC;

            internal BatteryInfo(bool present, bool isRunaway, bool overheating, double batteryLife, bool batteryDisabled, double scSoc)
            {
                Present = present;
                IsRunaway = isRunaway;
                Overheating = overheating;
                BatteryLife = batteryLife;
                BatteryDisabled = batteryDisabled;
                SC_SOC = scSoc;
            }

            internal static readonly BatteryInfo None = new BatteryInfo(false, false, false, 1.0, false, 1.0);
        }

        private struct BatteryFields
        {
            internal FieldInfo IsRunaway;       // bool
            internal FieldInfo BatteryLife;      // double, 0..1
            internal FieldInfo BatteryDisabled;  // bool
            internal FieldInfo SC_SOC;           // double, 0..1
            internal FieldInfo ChargeStatus;     // string ("BatteryChargeStatus") - OPTIONAL: missing on an older RealBattery just means Overheating always reads false, doesn't disable the whole bridge (same tolerance pattern as DangItBridge's ScreenName)
        }

        private static readonly Dictionary<Type, BatteryFields?> fieldCache = new Dictionary<Type, BatteryFields?>();

        internal static BatteryInfo GetInfo(Part part)
        {
            if (!part.Modules.Contains("RealBattery")) return BatteryInfo.None;

            PartModule module = part.Modules["RealBattery"];
            BatteryFields? fields = GetFields(module.GetType());
            if (fields == null) return BatteryInfo.None;

            try
            {
                bool isRunaway = (bool)fields.Value.IsRunaway.GetValue(module);
                double batteryLife = (double)fields.Value.BatteryLife.GetValue(module);
                bool batteryDisabled = (bool)fields.Value.BatteryDisabled.GetValue(module);
                double scSoc = (double)fields.Value.SC_SOC.GetValue(module);

                bool overheating = false;
                if (fields.Value.ChargeStatus != null)
                {
                    string chargeStatus = fields.Value.ChargeStatus.GetValue(module) as string;
                    overheating = chargeStatus == OverheatStatusText;
                }

                return new BatteryInfo(true, isRunaway, overheating, batteryLife, batteryDisabled, scSoc);
            }
            catch
            {
                return BatteryInfo.None;
            }
        }

        private static BatteryFields? GetFields(Type moduleType)
        {
            if (fieldCache.TryGetValue(moduleType, out BatteryFields? cached))
            {
                return cached;
            }

            FieldInfo isRunaway = moduleType.GetField("isRunaway", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo batteryLife = moduleType.GetField("BatteryLife", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo batteryDisabled = moduleType.GetField("BatteryDisabled", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo scSoc = moduleType.GetField("SC_SOC", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo chargeStatus = moduleType.GetField("BatteryChargeStatus", BindingFlags.Public | BindingFlags.Instance);

            BatteryFields? result = null;
            if (isRunaway != null && isRunaway.FieldType == typeof(bool)
                && batteryLife != null && batteryLife.FieldType == typeof(double)
                && batteryDisabled != null && batteryDisabled.FieldType == typeof(bool)
                && scSoc != null && scSoc.FieldType == typeof(double))
            {
                if (chargeStatus != null && chargeStatus.FieldType != typeof(string))
                {
                    chargeStatus = null;
                }
                result = new BatteryFields
                {
                    IsRunaway = isRunaway,
                    BatteryLife = batteryLife,
                    BatteryDisabled = batteryDisabled,
                    SC_SOC = scSoc,
                    ChargeStatus = chargeStatus
                };
            }

            fieldCache[moduleType] = result;
            return result;
        }
    }
}
