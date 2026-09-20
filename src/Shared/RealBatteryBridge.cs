using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;

namespace MFDExtension.Shared
{
    // The RealBattery reader: its own PartModule exposes what we need as
    // plain public fields, read by reflection like the other bridges.
    //
    // A RealBattery part carries both ElectricCharge (power) and StoredCharge
    // (energy, fixed 3600:1 to EC), so the generic FUEL average would blend
    // two scales into a meaningless number; SC_SOC, RealBattery's own scaled
    // state of charge, takes over for these parts entirely.
    internal static class RealBatteryBridge
    {
        // RealBattery's own EOL_THRESHOLD: the point at which it already
        // considers a cell end-of-life and starts notifying the player.
        internal const double EolThreshold = 0.80;

        // Comparing BatteryChargeStatus against this key is the only public
        // signal for "currently overheating": IsOverheating is internal, and
        // the public backing flags cover only one of the two chemistries.
        // Resolved through the game's Localizer, so it matches any install.
        private static string overheatStatusText;
        private static string OverheatStatusText => overheatStatusText ?? (overheatStatusText = Localizer.Format("#LOC_RB_Status_Overheat"));

        internal readonly struct BatteryInfo
        {
            internal readonly bool Present;
            internal readonly bool IsRunaway;
            internal readonly bool Overheating; // BatteryChargeStatus == OverheatStatusText
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
            internal FieldInfo ChargeStatus;     // string, OPTIONAL: if missing, Overheating just stays false
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
