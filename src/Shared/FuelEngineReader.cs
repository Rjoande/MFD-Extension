using System.Collections.Generic;
using System.IO;

namespace MFDExtension.Shared
{
    // The FUEL fraction blacklist: resources that behave like a stored
    // quantity but are NOT propulsion fuel, so their fill level must never
    // drive/dilute the FUEL severity bands (2026-08-26, from the Apollo XXI
    // test - a UniversalStorage shroud with SpareParts at 13% read Advisory
    // Magenta while every real propellant tank inside it was full).
    //
    // The real, user-editable list lives in a plain text file next to
    // VVEFIS.dll (2026-08-27, on the user's request - a recompile shouldn't
    // be required to add or remove a resource):
    //   GameData/MFDExtension/Extras/VVEFIS/FuelResourceBlacklist.txt
    // One resource name per line; "//" starts a line comment (rest of line
    // ignored); blank lines ignored. Edit that file, not this one, for a
    // day-to-day change - EmbeddedFallback below only exists in case the
    // file is missing or unreadable (manual/partial install, a locked
    // file), so the mod keeps working exactly as it did before this file
    // was externalized.
    internal static class FuelResourceBlacklist
    {
        private const string RelativePath = "GameData/MFDExtension/Extras/VVEFIS/FuelResourceBlacklist.txt";

        // Same list as the shipped .txt, frozen at the time of writing
        // (2026-08-26) - compiled by surveying every RESOURCE_DEFINITION
        // across this install's GameData and sorting by what the resource
        // IS, not by whether it happened to appear in one test craft. See
        // the .txt file itself for the categorized, commented version this
        // was generated from - keep the two in sync by hand if either one
        // is edited.
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

        // Loaded once, on first use - same per-frame-cost concern as every
        // other reflection cache in this project (DangItBridge/FARBridge/
        // RealBatteryBridge), just for a file read instead of a FieldInfo
        // lookup. A missing/renamed file degrades to EmbeddedFallback
        // silently, same "never throw on an optional external input"
        // convention used throughout this project.
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


    // Channel-neutral fuel/engine readings, moved from VVEFISSeverity.cs
    // into the shared basket (2026-08-24 refactor, "optional point 7"
    // approved by the user): nothing here touches VesselView or Unity
    // rendering, and sharing it makes the deferred CAS expansion to stock
    // conditions (log 43 - a separate design round, NOT yet built) trivial
    // when/if that round happens. Today only VVEFIS consumes this file.
    //
    // Band thresholds live here too: they define what "caution / empty"
    // MEAN, which must read the same on every channel - the colors those
    // bands map to stay per-channel.
    internal static class FuelEngineReader
    {
        // Dedicated tank-fill palette (2026-08-30, user's explicit call),
        // shared by BOTH the generic tank/resource FUEL reading (see
        // VVEFISSeverity.GetResourceOrEngineStatus) and RealBattery's
        // SC_SOC (see VVEFISSeverity.GetRealBatteryStatus) - restores the
        // parity the log 39 design originally called for between the two,
        // this time on this scheme instead of the old shared 4-band
        // Green/Cyan/Magenta/Yellow gradient (removed entirely, no other
        // caller left). Grounded against real-world low-fuel conventions
        // rather than picked arbitrarily: ~10% matches both the automotive
        // low-fuel light (commonly 1/8-1/16 tank) and the rough reserve
        // fraction behind a GA low-fuel light; <1% "functionally empty"
        // matches the FAR/CS-23.1337 certification concept that a fuel
        // gauge reads "zero" at the tank's calibrated unusable-fuel
        // residual, not at a literal zero. Verified with the user against
        // those standards before implementing, not assumed.
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

        // Replicates VesselViewer.cs's own COLORMODE.FUEL formula: average
        // fill fraction across every resource the part carries, skipping
        // near-empty ones (<= 2 units, same threshold VV uses) from the
        // average the same way VV does. Returns false if the part carries no
        // resources at all (a structural part, not a tank) - that's a "no
        // data" case, not a "zero fuel" case.
        //
        // ALSO returns false when the part carries no resource with real
        // storage CAPACITY (fix 2026-08-24, from the first in-game
        // screenshot): stock air intakes carry an IntakeAir resource whose
        // maxAmount is tiny - a live buffer, not real storage - so its
        // amount is ALWAYS under the <= 2.0 skip threshold. The old code
        // still returned true with fraction 0 ("empty tank"), painting
        // every healthy intake CautionYellow. The tell is maxAmount, not
        // amount: a micro-buffer can never contribute to the average no
        // matter how full it is, while a genuinely drained real tank
        // (maxAmount large, amount ~0) must keep reading as "empty" - not
        // vanish into "no data". Same 2.0 threshold on both for symmetry
        // with VV's own skip rule.
        //
        // FuelResourceBlacklist.IsExcluded resources (fix 2026-08-26, from
        // the Apollo XXI test) are skipped BEFORE any of the above - they
        // never enter resCount, the average, or the anyRealStorage check,
        // exactly as if the part didn't carry them at all. Without this, a
        // part whose only "big" resource is SpareParts/Snacks/etc. would
        // read a FUEL level driven entirely by a resource that has nothing
        // to do with propulsion (the UniversalStorage shroud case: 13%
        // spare parts, 100% real propellant, colored by the former).
        //
        // EXCEPT (refinement 2026-08-27, user's explicit call): the
        // blacklist only applies when a part ALSO carries other active
        // resources to be diluted by. If a blacklisted resource is the
        // part's ONLY active one - a dedicated Ore or Snacks container,
        // say - its fill level IS the useful reading (there's nothing else
        // it could be confused with, and nothing for it to crowd out), so
        // it counts as fuel after all. "Active" here means flow-enabled
        // (see the flowState check right below) - a disabled resource
        // doesn't count toward "the only one" either.
        //
        // A resource with flowState == false (the player used "Disable X
        // Flow", e.g. to run a tank as inert ballast/structural mass) is
        // skipped ENTIRELY - not counted toward resCount, the average, OR
        // whether another resource on the same part is "the only one".
        // Before this, a deliberately-emptied, deliberately-disabled tank
        // still read as "empty" (Yellow, or Amber+red border if it fed an
        // engine that consequently reads deprived) - a false alarm for a
        // resource the player explicitly took out of service, not a fault.
        //
        // KNOWN, NOT YET FIXED (2026-08-26, deferred at the user's explicit
        // request - documented, not silently dropped): resources that
        // SURVIVE this filter but sit near-empty are still skipped from the
        // numerator while remaining in resCount (the denominator) - a part
        // with one full genuine tank plus one genuine-but-drained one still
        // reads a diluted average, same class of issue as the intake bug
        // but for a real (not micro-buffer) resource. Not implicated in the
        // Apollo XXI test (each affected part carried exactly one counted
        // resource), so left alone until a real case demonstrates it.
        internal static bool TryGetFuelFraction(Part part, out float fraction)
        {
            fraction = 0f;

            // Pass 1: how many resources are even in play (flow-enabled)?
            // A disabled resource doesn't exist for fuel-reading purposes -
            // not "empty", just not participating - so it can't count
            // toward "the only active resource" either.
            int activeCount = 0;
            foreach (PartResource resource in part.Resources.dict.Values)
            {
                if (resource.flowState) activeCount++;
            }
            if (activeCount == 0) return false;
            bool onlyActiveResource = activeCount == 1;

            // Pass 2: among the active resources, how many survive the
            // blacklist (skipped only when NOT the part's only active
            // resource - see the header comment above), and does at least
            // one have real storage capacity? The divisor must be this
            // FINAL count, not a running one, hence the separate pass.
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

        // Replicates VesselViewer.cs's own renderEngineThrusts() propellant-
        // deprivation logic (same resource name strings, same priority order
        // NOFUEL > NOPOWER > NOAIR), extended with the flameout/EngineIgnited
        // split confirmed on the real decompiled source (2026-08-19):
        // ModuleEngines.isOperational is literally "!flameout && EngineIgnited"
        // - it cannot on its own distinguish "never ignited" from "flamed out",
        // both read isOperational=false. Reading the two backing fields
        // directly (both public) is what makes the split possible.
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
