using System.Collections.Generic;
using System.IO;

namespace MFDExtension.Shared
{
    // Resources that behave like a stored quantity but are NOT propulsion
    // fuel, so their fill level must never drive or dilute the FUEL bands.
    //
    // The live list is the user-editable text file below (one name per line,
    // "//" starts a comment); EmbeddedFallback is a copy of it used only when
    // that file is missing or unreadable. Keep the two in sync by hand.
    internal static class FuelResourceBlacklist
    {
        private const string RelativePath = "GameData/MFDExtension/Extras/VVEFIS/FuelResourceBlacklist.txt";

        private static readonly string[] EmbeddedFallback =
        {
            "ElectricCharge", "StoredCharge", "Megajoules", "ThermalPower", "WasteHeat",
            "Snacks", "Soil", "Food", "Water", "WasteWater", "Waste",
            "Oxygen", "CarbonDioxide", "Fertilizer", "Mulch", "Supplies", "ColonySupplies",
            "SpareParts", "RocketParts", "MaterialKits", "SpecializedParts", "Machinery", "Recyclables",
            "Ablator",
            "Ore", "MetallicOre", "Minerals", "ExoticMinerals", "RareMetals", "RefinedExotics",
            "Metals", "Substrate", "Organics", "Rock", "Dirt", "Gypsum", "Hydrates", "Lead",
            "Chemicals", "Silicates", "Silicon", "Regolith", "Monazite", "Spodumene", "Fluorite",
            "Nitratine", "Salt", "Sodium", "Calcium", "Magnesium", "Aluminium", "Alumina",
            "Potassium", "Borate", "Caesium", "Chlorine", "Fluorine", "Carbon",
            "DepletedFuel", "DepletedUranium", "Uraninite", "Polymers", "Actinides",
            "IntakeAir", "IntakeAtm", "IntakeLqd", "CompressedAir", "RamPower",
            "ChargedParticles", "SolarWind", "VacuumPlasma", "ExoticMatter",
            "bd_filmStock", "FSCoolant", "MJPropellant", "BallastTons",
        };

        // Read once, on first use; never throws on an optional external input.
        private static HashSet<string> cached;

        internal static bool IsExcluded(string resourceName)
        {
            if (cached == null) cached = Load();
            return cached.Contains(resourceName);
        }

        private static HashSet<string> Load()
        {
            try
            {
                string path = KSPUtil.ApplicationRootPath + RelativePath;
                if (File.Exists(path))
                {
                    HashSet<string> fromFile = new HashSet<string>();
                    foreach (string rawLine in File.ReadAllLines(path))
                    {
                        int commentAt = rawLine.IndexOf("//");
                        string line = (commentAt >= 0 ? rawLine.Substring(0, commentAt) : rawLine).Trim();
                        if (line.Length > 0) fromFile.Add(line);
                    }
                    if (fromFile.Count > 0) return fromFile;
                }
            }
            catch
            {
                // fall through to the embedded fallback below
            }
            return new HashSet<string>(EmbeddedFallback);
        }
    }


    // Channel-neutral fuel and engine readings: nothing here touches
    // VesselView or Unity rendering. The band thresholds live here because
    // they define what "caution / empty" MEAN, which must read the same on
    // every channel; the colors those bands map to stay per-channel.
    internal static class FuelEngineReader
    {
        // Tank-fill bands, shared by the generic FUEL reading and by
        // RealBattery's SC_SOC. ~10% is the usual low-fuel warning point;
        // below 1% is "functionally empty", where a real gauge reads zero.
        internal const float TankCautionThreshold = 0.10f;
        internal const float TankEmptyThreshold = 0.01f;

        internal enum EngineCondition
        {
            None,
            NoFuel,
            NoPower,
            NoAir,
            Active,
            Ready,
            NotYetActivated, // never ignited - normal, e.g. an unstaged upper stage
            FlamedOut        // ignited at some point, then flamed out / shut down - a real event
        }

        // Replicates VesselViewer's own COLORMODE.FUEL formula: average fill
        // fraction across the part's resources, skipping near-empty ones
        // (<= 2 units, VV's threshold). False means "no data" - a structural
        // part, not an empty tank.
        internal static bool TryGetFuelFraction(Part part, out float fraction)
        {
            fraction = 0f;

            // Pass 1: which resources are in play at all. A flow-disabled one
            // (a tank run as inert ballast) doesn't exist for fuel purposes,
            // so it can't count towards "the only active resource" either.
            int activeCount = 0;
            foreach (PartResource resource in part.Resources.dict.Values)
            {
                if (resource.flowState) activeCount++;
            }
            if (activeCount == 0) return false;
            bool onlyActiveResource = activeCount == 1;

            // Pass 2: the final divisor, which is why it can't be counted on
            // the fly. A blacklisted resource is skipped unless it is the
            // part's only active one (then its level IS the useful reading);
            // anyRealStorage keeps an intake's micro-buffer, always below the
            // skip threshold, from reading as an empty tank.
            bool anyRealStorage = false;
            int resCount = 0;
            foreach (PartResource resource in part.Resources.dict.Values)
            {
                if (!resource.flowState) continue;
                if (!onlyActiveResource && FuelResourceBlacklist.IsExcluded(resource.resourceName)) continue;
                resCount++;
                if (resource.maxAmount > 2.0) anyRealStorage = true;
            }
            if (resCount == 0 || !anyRealStorage) return false;

            // Known and not fixed: a near-empty resource is skipped here yet
            // still counts in resCount, so one full and one drained genuine
            // tank on the same part average out as diluted.
            double totalFraction = 0.0;
            foreach (PartResource resource in part.Resources.dict.Values)
            {
                if (!resource.flowState) continue;
                if (!onlyActiveResource && FuelResourceBlacklist.IsExcluded(resource.resourceName)) continue;
                if (resource.amount <= 2.0) continue;
                totalFraction += (resource.amount / resource.maxAmount) / resCount;
            }

            fraction = (float)totalFraction;
            return true;
        }

        // Replicates VesselViewer's own propellant-deprivation logic (same
        // resource names, same NOFUEL > NOPOWER > NOAIR order). isOperational
        // is "!flameout && EngineIgnited" and so cannot tell "never ignited"
        // from "flamed out" - the two backing fields, both public, can.
        internal static EngineCondition GetEngineCondition(Part part)
        {
            List<Propellant> propellants = null;
            float maxThrust = 0f;
            float finalThrust = 0f;
            bool engineIgnited = false;
            bool flamedOut = false;

            if (part.Modules.Contains("ModuleEngines"))
            {
                ModuleEngines engineModule = (ModuleEngines)part.Modules["ModuleEngines"];
                propellants = engineModule.propellants;
                maxThrust = engineModule.maxThrust;
                finalThrust = engineModule.finalThrust;
                engineIgnited = engineModule.EngineIgnited;
                flamedOut = engineModule.flameout;
            }
            else if (part.Modules.Contains("ModuleEnginesFX"))
            {
                ModuleEnginesFX engineModule = (ModuleEnginesFX)part.Modules["ModuleEnginesFX"];
                propellants = engineModule.propellants;
                maxThrust = engineModule.maxThrust;
                finalThrust = engineModule.finalThrust;
                engineIgnited = engineModule.EngineIgnited;
                flamedOut = engineModule.flameout;
            }

            if (propellants == null) return EngineCondition.None;

            bool deprivedLiquidFuel = false, deprivedOxidizer = false, deprivedSolidFuel = false;
            bool deprivedIntakeAir = false, deprivedMonoPropellant = false, deprivedXenonGas = false;
            bool deprivedElectricCharge = false;

            foreach (Propellant propellant in propellants)
            {
                if (!propellant.isDeprived) continue;
                switch (propellant.name)
                {
                    case "LiquidFuel": deprivedLiquidFuel = true; break;
                    case "Oxidizer": deprivedOxidizer = true; break;
                    case "SolidFuel": deprivedSolidFuel = true; break;
                    case "IntakeAir": deprivedIntakeAir = true; break;
                    case "MonoPropellant": deprivedMonoPropellant = true; break;
                    case "XenonGas": deprivedXenonGas = true; break;
                    case "ElectricCharge": deprivedElectricCharge = true; break;
                }
            }

            if (deprivedLiquidFuel || deprivedSolidFuel || deprivedMonoPropellant || deprivedXenonGas || deprivedOxidizer)
                return EngineCondition.NoFuel;
            if (deprivedElectricCharge)
                return EngineCondition.NoPower;
            if (deprivedIntakeAir)
                return EngineCondition.NoAir;

            float scale = maxThrust > 0f ? finalThrust / maxThrust : 0f;
            if (scale >= 0.01f) return EngineCondition.Active;

            if (flamedOut) return EngineCondition.FlamedOut;
            if (!engineIgnited) return EngineCondition.NotYetActivated;
            return EngineCondition.Ready;
        }
    }
}
