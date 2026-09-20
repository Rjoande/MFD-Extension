using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MFDExtension.Shared
{
    // One member of a heat loop, as the loop simulation sees it this frame.
    // Flux > 0: a source, its totalSystemFlux [kW]. Flux <= 0: a sink, its
    // consumedSystemFlux - what the loop actually allocated to it by priority
    // (user's decision 2026-09-16, survey section 4.2: a radiator reading
    // 0 kW in a balanced loop is spare capacity, and should read as such).
    internal struct HeatMember
    {
        public string Title;
        public float Flux;
    }

    internal sealed class HeatLoopInfo
    {
        public int Id;
        public float Temperature;        // K
        public float NominalTemperature; // K
        public float NetFlux;            // kW, SystemHeat's own HeatLoop.NetFlux
        public float Generated;          // kW, sum of the used members' positive totalSystemFlux
        public float Rejected;           // kW, magnitude of the used members' negative totalSystemFlux
        public int Idle;                 // used members with |totalSystemFlux| below the threshold, not listed
        public readonly List<HeatMember> Members = new List<HeatMember>(); // sources by magnitude, then sinks by magnitude
    }

    internal enum ReactorKind
    {
        Fission, // ModuleSystemHeatFissionReactor and its FissionEngine subclass (SystemHeat)
        Fusion,  // FusionReactor and its ModuleFusionEngine subclass (FarFutureTechnologies), read through SystemHeat's own reactor-panel fields
    }

    internal enum ReactorState
    {
        Off,
        On,
        Hibernating,
        Charging, // fusion only: capacitors charging with the reactor still off
        Scram,    // INFERRED: off with the core still above the safety override - SystemHeat keeps no flag for it (user's call 2026-09-18)
        Meltdown, // CoreIntegrity <= 0
    }

    internal sealed class ReactorInfo
    {
        public ReactorKind Kind;
        public string Title;
        public ReactorState State;
        public float Power;              // Ec/s
        public float Heat;               // kW
        public float Throttle;           // %, fission only (CurrentThrottle, the actual one)
        public float CoreTemperature;    // K; fusion: SystemOutletTemperature
        public float NominalTemperature; // K, fission only
        public float CriticalTemperature;// K, fission only
        public float Integrity = 100f;   // %, fission only
        public double LifeSeconds = double.NaN; // fission: fuel time at the current throttle; NaN = not computable / reactor not burning
        public double FuelFraction = double.NaN; // fission: FuelName amount / maxAmount; NaN = unknown
        public float ChargeFraction = float.NaN; // fusion: CurrentCharge / ChargeGoal
    }

    internal sealed class SystemHeatSnapshot
    {
        public bool HasData;             // false: SystemHeat present but its simulator hasn't built this vessel's loops yet
        public readonly List<HeatLoopInfo> Loops = new List<HeatLoopInfo>();   // by ID
        public readonly List<ReactorInfo> Reactors = new List<ReactorInfo>();  // fission first, in part order
        public float Generated;          // kW, over all loops
        public float Rejected;           // kW, magnitude
        public int CryoTanks;            // tanks that carry a boil-off resource
        public int Boiloff;              // of those, currently boiling off
        public readonly List<string> BoiloffTanks = new List<string>(); // their titles, for CAS
        public int Sinks;
        public float SinkStored;         // kJ
        public float SinkCapacity;       // kJ
        public float IdleThreshold = DefaultIdleThreshold; // kW, SystemHeatSettings.AbsFluxThreshold when readable

        public const float DefaultIdleThreshold = 0.5f;
    }

    // THE SystemHeat reader (2026-09-18, CLAUDE.md log 89). Lives in the
    // shared basket by decision (survey section 4.3, point 9): the TCS bay is
    // its first consumer, CAS's thermal entries come in a later round on the
    // same snapshot. Every member below was read on the decompiled 0.9.1
    // assembly (the version on the user's install); reflection only, no
    // compile-time dependency; every failure degrades to "no data".
    //
    // Facts that shaped this file, all verified on the source:
    //  - SystemHeatVessel.Simulator.HeatLoops is NULL until the simulator's
    //    first Reset (which happens when the vessel becomes the active one)
    //    and is replaced wholesale on every vessel-modified/dock/undock
    //    event - never cache the list, only the members that fetch it.
    //  - HeatLoop members are PROPERTIES; ModuleSystemHeat members are
    //    public FIELDS; SystemHeatSettings.* are public STATIC fields.
    //  - HeatLoop.NetFlux/Positive/Negative and the loop temperature only
    //    count members with moduleUsed == true (a cryo tank with cooling off
    //    unregisters itself that way), whereas the simulator's own
    //    TotalHeatGeneration/Rejection count every member. Our totals follow
    //    moduleUsed so that the loop rows always add up to the header.
    //  - Every *Status / *UI string on SystemHeat's modules is refreshed only
    //    while the part's PAW is open - stale in IVA by construction. Only
    //    numeric members are read here.
    //  - The reactor's burn rate and input list are protected; the core life
    //    is recomputed from the part's config node the same way the reactor
    //    does it (amount / (throttle/100 * INPUT_RESOURCE.Ratio)), see
    //    ReadFissionLife.
    internal static class SystemHeatReader
    {
        internal const string AssemblyName = "SystemHeat";

        // SystemHeat's own thresholds, shared by the TCS pages and CAS's
        // thermal entries (verified on 0.9.1: ToolbarPanelLoopWidget's
        // pulsing borders, LoopTemperatureTest's "critical" level).
        internal const float HeatingFlux = 0.05f;    // kW: net flux above this = loop still heating
        internal const float OvertempMargin = 0.5f;  // K above nominal = overtemp (red border)
        internal const float CriticalDelta = 500f;   // K above nominal = Engineer's Report "critical"

        // ---- shared snapshot ------------------------------------------------
        // One reflection pass per vessel at most every RefreshSeconds, shared
        // by every consumer (the three TCS pages, CAS) and every monitor.
        // SnapshotVersion bumps on each re-read so consumers can key their
        // own render caches on it. Real time, so warp/pause don't matter.
        private const float RefreshSeconds = 0.25f;
        private static readonly SystemHeatSnapshot cached = new SystemHeatSnapshot();
        private static Guid cachedVessel = Guid.Empty;
        private static float cachedTime = -1f;
        private static int cachedVersion;

        internal static int SnapshotVersion
        {
            get { return cachedVersion; }
        }

        internal static SystemHeatSnapshot GetSnapshot(Vessel vessel)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (vessel.id != cachedVessel || cachedTime < 0f || now - cachedTime >= RefreshSeconds || now < cachedTime)
            {
                Read(vessel, cached);
                cachedVessel = vessel.id;
                cachedTime = now;
                cachedVersion++;
            }
            return cached;
        }

        private static bool resolved;
        private static bool available;

        private static Type vesselModuleType;
        private static PropertyInfo vesselSimulator;
        private static PropertyInfo simulatorLoops;
        private static PropertyInfo loopId, loopTemperature, loopNominal, loopNetFlux, loopModules;
        private static FieldInfo moduleTotalFlux, moduleConsumedFlux, moduleUsed;
        private static FieldInfo settingsIdleThreshold;

        private static Type fissionType;
        private static FieldInfo fEnabled, fHibernating, fReactorThrottle, fThrottle, fPower, fHeat, fCore, fNominal, fCritical,
                                 fSafetyOverride, fIntegrity, fFuelName;

        private static Type cryoType;
        private static FieldInfo cryoBoiloff;
        private static PropertyInfo cryoHasBoiloffResource;

        private static Type sinkType;
        private static FieldInfo sinkStored, sinkMax;

        private static MethodInfo getModuleConfigNode; // ModuleUtils.GetModuleConfigNode(Part, string), public static

        // FarFutureTechnologies' fusion reactors, by module name exactly as
        // SystemHeat's own reactor panel finds them; their fields are resolved
        // per concrete Type on first sight (no FFT assembly lookup needed).
        private sealed class FusionFields
        {
            public FieldInfo Enabled, Charging, CurrentCharge, ChargeGoal, SystemPower, PowerProduced, OutletTemperature;
        }
        private static readonly Dictionary<Type, FusionFields> fusionFields = new Dictionary<Type, FusionFields>();

        // part.flightID -> INPUT_RESOURCE.Ratio of the reactor's FuelName
        // (<= 0: config unreadable, fall back to FUEL %). Never invalidated:
        // a part's config does not change within a session.
        private static readonly Dictionary<uint, double> fuelRatioCache = new Dictionary<uint, double>();

        internal static bool IsAvailable
        {
            get
            {
                Resolve();
                return available;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            Assembly assembly = null;
            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                // CLR name, never LoadedAssembly.name - see ModPresence.
                if (loaded.assembly.GetName().Name == AssemblyName)
                {
                    assembly = loaded.assembly;
                    break;
                }
            }
            if (assembly == null) return;

            try
            {
                const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
                const BindingFlags stat = BindingFlags.Public | BindingFlags.Static;

                vesselModuleType = assembly.GetType("SystemHeat.SystemHeatVessel");
                Type simulatorType = assembly.GetType("SystemHeat.SystemHeatSimulator");
                Type loopType = assembly.GetType("SystemHeat.HeatLoop");
                Type moduleType = assembly.GetType("SystemHeat.ModuleSystemHeat");
                if (vesselModuleType == null || simulatorType == null || loopType == null || moduleType == null) return;

                vesselSimulator = vesselModuleType.GetProperty("Simulator", inst);
                simulatorLoops = simulatorType.GetProperty("HeatLoops", inst);
                loopId = loopType.GetProperty("ID", inst);
                loopTemperature = loopType.GetProperty("Temperature", inst);
                loopNominal = loopType.GetProperty("NominalTemperature", inst);
                loopNetFlux = loopType.GetProperty("NetFlux", inst);
                loopModules = loopType.GetProperty("LoopModules", inst);
                moduleTotalFlux = moduleType.GetField("totalSystemFlux", inst);
                moduleConsumedFlux = moduleType.GetField("consumedSystemFlux", inst);
                moduleUsed = moduleType.GetField("moduleUsed", inst);
                if (vesselSimulator == null || simulatorLoops == null || loopId == null || loopTemperature == null
                    || loopNominal == null || loopNetFlux == null || loopModules == null
                    || moduleTotalFlux == null || moduleConsumedFlux == null || moduleUsed == null) return;

                // Everything below is optional: a missing member costs one
                // page section (reactors, cryo, sinks) or one refinement
                // (the idle threshold, the core life), never the loops.
                Type settingsType = assembly.GetType("SystemHeat.SystemHeatSettings");
                if (settingsType != null) settingsIdleThreshold = settingsType.GetField("AbsFluxThreshold", stat);

                fissionType = assembly.GetType("SystemHeat.ModuleSystemHeatFissionReactor");
                if (fissionType != null)
                {
                    fEnabled = fissionType.GetField("Enabled", inst);
                    fHibernating = fissionType.GetField("Hibernating", inst);
                    fReactorThrottle = fissionType.GetField("CurrentReactorThrottle", inst);
                    fThrottle = fissionType.GetField("CurrentThrottle", inst);
                    fPower = fissionType.GetField("CurrentElectricalGeneration", inst);
                    fHeat = fissionType.GetField("CurrentHeatGeneration", inst);
                    fCore = fissionType.GetField("InternalCoreTemperature", inst);
                    fNominal = fissionType.GetField("NominalTemperature", inst);
                    fCritical = fissionType.GetField("CriticalTemperature", inst);
                    fSafetyOverride = fissionType.GetField("CurrentSafetyOverride", inst);
                    fIntegrity = fissionType.GetField("CoreIntegrity", inst);
                    fFuelName = fissionType.GetField("FuelName", inst);
                    if (fEnabled == null || fPower == null || fHeat == null || fCore == null || fNominal == null) fissionType = null;
                }

                cryoType = assembly.GetType("SystemHeat.ModuleSystemHeatCryoTank");
                if (cryoType != null)
                {
                    cryoBoiloff = cryoType.GetField("BoiloffOccuring", inst);
                    cryoHasBoiloffResource = cryoType.GetProperty("HasAnyBoiloffResource", inst);
                    if (cryoBoiloff == null) cryoType = null;
                }

                sinkType = assembly.GetType("SystemHeat.ModuleSystemHeatSink");
                if (sinkType != null)
                {
                    sinkStored = sinkType.GetField("heatStored", inst);
                    sinkMax = sinkType.GetField("heatStorageMaximum", inst);
                    if (sinkStored == null || sinkMax == null) sinkType = null;
                }

                Type utilsType = assembly.GetType("SystemHeat.ModuleUtils");
                if (utilsType != null)
                {
                    getModuleConfigNode = utilsType.GetMethod("GetModuleConfigNode", stat, null, new[] { typeof(Part), typeof(string) }, null);
                }

                available = true;
            }
            catch
            {
                available = false;
            }
        }

        // Fills `snapshot` from scratch. Returns false (HasData false) when
        // SystemHeat is installed but has no loops built for this vessel yet.
        internal static bool Read(Vessel vessel, SystemHeatSnapshot snapshot)
        {
            snapshot.HasData = false;
            snapshot.Loops.Clear();
            snapshot.Reactors.Clear();
            snapshot.Generated = 0f;
            snapshot.Rejected = 0f;
            snapshot.CryoTanks = 0;
            snapshot.Boiloff = 0;
            snapshot.BoiloffTanks.Clear();
            snapshot.Sinks = 0;
            snapshot.SinkStored = 0f;
            snapshot.SinkCapacity = 0f;

            Resolve();
            if (!available || vessel == null) return false;

            object simulatorVessel = null;
            List<VesselModule> vesselModules = vessel.vesselModules;
            if (vesselModules == null) return false;
            for (int i = 0; i < vesselModules.Count; ++i)
            {
                VesselModule vesselModule = vesselModules[i];
                if (vesselModule != null && vesselModuleType.IsInstanceOfType(vesselModule))
                {
                    simulatorVessel = vesselModule;
                    break;
                }
            }
            if (simulatorVessel == null) return false;

            IList loops;
            try
            {
                object simulator = vesselSimulator.GetValue(simulatorVessel, null);
                if (simulator == null) return false;
                loops = simulatorLoops.GetValue(simulator, null) as IList; // null until the first Reset
            }
            catch
            {
                return false;
            }
            if (loops == null) return false;

            snapshot.IdleThreshold = ReadIdleThreshold();

            for (int i = 0; i < loops.Count; ++i)
            {
                object loop = loops[i];
                if (loop == null) continue;
                HeatLoopInfo info = ReadLoop(loop, snapshot.IdleThreshold);
                if (info == null) continue;
                snapshot.Loops.Add(info);
                snapshot.Generated += info.Generated;
                snapshot.Rejected += info.Rejected;
            }
            snapshot.Loops.Sort((a, b) => a.Id.CompareTo(b.Id));

            ReadParts(vessel, snapshot);

            snapshot.HasData = true;
            return true;
        }

        private static float ReadIdleThreshold()
        {
            if (settingsIdleThreshold == null) return SystemHeatSnapshot.DefaultIdleThreshold;
            try
            {
                float value = (float)settingsIdleThreshold.GetValue(null);
                return value > 0f ? value : SystemHeatSnapshot.DefaultIdleThreshold;
            }
            catch
            {
                return SystemHeatSnapshot.DefaultIdleThreshold;
            }
        }

        private static readonly List<HeatMember> sinkBuffer = new List<HeatMember>();

        private static HeatLoopInfo ReadLoop(object loop, float idleThreshold)
        {
            HeatLoopInfo info = new HeatLoopInfo();
            IList modules;
            try
            {
                info.Id = (int)loopId.GetValue(loop, null);
                info.Temperature = (float)loopTemperature.GetValue(loop, null);
                info.NominalTemperature = (float)loopNominal.GetValue(loop, null);
                info.NetFlux = (float)loopNetFlux.GetValue(loop, null);
                modules = loopModules.GetValue(loop, null) as IList;
            }
            catch
            {
                return null;
            }
            if (modules == null) return info;

            sinkBuffer.Clear();
            for (int i = 0; i < modules.Count; ++i)
            {
                PartModule module = modules[i] as PartModule;
                if (module == null || module.part == null) continue;

                float total, consumed;
                bool used;
                try
                {
                    used = (bool)moduleUsed.GetValue(module);
                    total = (float)moduleTotalFlux.GetValue(module);
                    consumed = (float)moduleConsumedFlux.GetValue(module);
                }
                catch
                {
                    continue;
                }
                if (!used) continue; // not part of the simulation right now (cooling switched off, etc.)
                if (float.IsNaN(total) || float.IsInfinity(total)) continue;

                // Idle by CAPACITY (totalSystemFlux), not by allocation: a
                // radiator with capacity the loop doesn't need this frame
                // is listed at 0 kW as spare capacity, a battery's zero-flux
                // volume-only module is idle and only counted.
                if (Math.Abs(total) < idleThreshold)
                {
                    info.Idle++;
                    continue;
                }

                string title = module.part.partInfo != null ? module.part.partInfo.title : module.part.name;
                if (total > 0f)
                {
                    info.Generated += total;
                    info.Members.Add(new HeatMember { Title = title, Flux = total });
                }
                else
                {
                    info.Rejected -= total;
                    if (float.IsNaN(consumed) || float.IsInfinity(consumed) || consumed > 0f) consumed = 0f;
                    sinkBuffer.Add(new HeatMember { Title = title, Flux = consumed });
                }
            }

            info.Members.Sort(CompareByMagnitude);
            sinkBuffer.Sort(CompareByMagnitude);
            info.Members.AddRange(sinkBuffer);
            return info;
        }

        private static int CompareByMagnitude(HeatMember a, HeatMember b)
        {
            int byValue = Math.Abs(b.Flux).CompareTo(Math.Abs(a.Flux));
            return byValue != 0 ? byValue : string.CompareOrdinal(a.Title, b.Title);
        }

        // Reactors, cryo tanks and heat sinks: one pass over the vessel's
        // parts, each module matched by type (fission) or by module name
        // (fusion, FFT's own types).
        private static void ReadParts(Vessel vessel, SystemHeatSnapshot snapshot)
        {
            List<Part> parts = vessel.parts;
            if (parts == null) return;

            for (int p = 0; p < parts.Count; ++p)
            {
                Part part = parts[p];
                if (part == null || part.Modules == null) continue;
                for (int m = 0; m < part.Modules.Count; ++m)
                {
                    PartModule module = part.Modules[m];
                    if (module == null) continue;
                    try
                    {
                        if (fissionType != null && fissionType.IsInstanceOfType(module))
                        {
                            ReactorInfo reactor = ReadFission(module);
                            if (reactor != null) snapshot.Reactors.Add(reactor);
                        }
                        else if (module.moduleName == "FusionReactor" || module.moduleName == "ModuleFusionEngine")
                        {
                            ReactorInfo reactor = ReadFusion(module);
                            if (reactor != null) snapshot.Reactors.Add(reactor);
                        }
                        else if (cryoType != null && cryoType.IsInstanceOfType(module))
                        {
                            bool hasResource = true;
                            if (cryoHasBoiloffResource != null) hasResource = (bool)cryoHasBoiloffResource.GetValue(module, null);
                            if (!hasResource) continue;
                            snapshot.CryoTanks++;
                            if ((bool)cryoBoiloff.GetValue(module))
                            {
                                snapshot.Boiloff++;
                                snapshot.BoiloffTanks.Add(PartTitle(module));
                            }
                        }
                        else if (sinkType != null && sinkType.IsInstanceOfType(module))
                        {
                            snapshot.Sinks++;
                            snapshot.SinkStored += (float)sinkStored.GetValue(module);
                            snapshot.SinkCapacity += (float)sinkMax.GetValue(module);
                        }
                    }
                    catch
                    {
                        // one unreadable module must not cost the others
                    }
                }
            }

            // Fission first, then fusion, each in part order.
            snapshot.Reactors.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        }

        private static string PartTitle(PartModule module)
        {
            return module.part.partInfo != null ? module.part.partInfo.title : module.part.name;
        }

        private static ReactorInfo ReadFission(PartModule module)
        {
            ReactorInfo info = new ReactorInfo { Kind = ReactorKind.Fission, Title = PartTitle(module) };

            bool enabled = (bool)fEnabled.GetValue(module);
            bool hibernating = fHibernating != null && (bool)fHibernating.GetValue(module);
            info.Power = (float)fPower.GetValue(module);
            info.Heat = (float)fHeat.GetValue(module);
            info.Throttle = fThrottle != null ? (float)fThrottle.GetValue(module) : 0f;
            info.CoreTemperature = (float)fCore.GetValue(module);
            info.NominalTemperature = (float)fNominal.GetValue(module);
            info.CriticalTemperature = fCritical != null ? (float)fCritical.GetValue(module) : float.MaxValue;
            info.Integrity = fIntegrity != null ? (float)fIntegrity.GetValue(module) : 100f;
            float safetyOverride = fSafetyOverride != null ? (float)fSafetyOverride.GetValue(module) : float.MaxValue;

            if (info.Integrity <= 0f) info.State = ReactorState.Meltdown;
            else if (enabled) info.State = hibernating ? ReactorState.Hibernating : ReactorState.On;
            else if (info.CoreTemperature > safetyOverride) info.State = ReactorState.Scram; // inferred, see ReactorState
            else info.State = ReactorState.Off;

            ReadFissionFuel(module, info, enabled);
            return info;
        }

        // Core life = fuel amount / burn rate, burn rate = (setpoint / 100) *
        // INPUT_RESOURCE.Ratio - the reactor's own HandleResourceActivities
        // formula (burnRate itself is protected). The Ratio comes from the
        // part's config node through SystemHeat's public
        // ModuleUtils.GetModuleConfigNode (which already try/catches its
        // Single() and returns null on any doubt), cached per part.
        private static void ReadFissionFuel(PartModule module, ReactorInfo info, bool enabled)
        {
            if (fFuelName == null) return;
            string fuelName = fFuelName.GetValue(module) as string;
            if (string.IsNullOrEmpty(fuelName)) return;

            PartResource resource = module.part.Resources != null ? module.part.Resources.Get(fuelName) : null;
            if (resource == null) return;
            if (resource.maxAmount > 0.0) info.FuelFraction = resource.amount / resource.maxAmount;

            double ratio;
            if (!fuelRatioCache.TryGetValue(module.part.flightID, out ratio))
            {
                ratio = ReadFuelRatio(module, fuelName);
                fuelRatioCache[module.part.flightID] = ratio;
            }
            if (ratio <= 0.0) return;

            float setpoint = fReactorThrottle != null ? (float)fReactorThrottle.GetValue(module) : 0f;
            if (!enabled || setpoint <= 0f) return; // not burning: life is not a number, the caller shows FUEL %
            double burnRate = setpoint / 100.0 * ratio;
            if (burnRate < 1e-15) return;
            info.LifeSeconds = resource.amount / burnRate;
        }

        private static double ReadFuelRatio(PartModule module, string fuelName)
        {
            if (getModuleConfigNode == null) return -1.0;
            try
            {
                ConfigNode node = getModuleConfigNode.Invoke(null, new object[] { module.part, module.moduleName }) as ConfigNode;
                if (node == null) return -1.0;
                ConfigNode[] inputs = node.GetNodes("INPUT_RESOURCE");
                for (int i = 0; i < inputs.Length; ++i)
                {
                    if (inputs[i].GetValue("ResourceName") != fuelName) continue;
                    double ratio;
                    if (double.TryParse(inputs[i].GetValue("Ratio"), System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out ratio)) return ratio;
                    return -1.0;
                }
            }
            catch
            {
                // fall through
            }
            return -1.0;
        }

        // FFT's FusionReactor (ModuleFusionEngine derives from it): the same
        // public fields SystemHeat's ReactorWidget reads through
        // Fields.GetValue, verified on the decompiled FFT assembly 2026-09-18.
        private static ReactorInfo ReadFusion(PartModule module)
        {
            Type type = module.GetType();
            FusionFields fields;
            if (!fusionFields.TryGetValue(type, out fields))
            {
                const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
                fields = new FusionFields
                {
                    Enabled = type.GetField("Enabled", inst),
                    Charging = type.GetField("Charging", inst),
                    CurrentCharge = type.GetField("CurrentCharge", inst),
                    ChargeGoal = type.GetField("ChargeGoal", inst),
                    SystemPower = type.GetField("SystemPower", inst),
                    PowerProduced = type.GetField("CurrentPowerProduced", inst),
                    OutletTemperature = type.GetField("SystemOutletTemperature", inst),
                };
                fusionFields[type] = fields;
            }
            if (fields.Enabled == null) return null; // not the module we think it is

            ReactorInfo info = new ReactorInfo { Kind = ReactorKind.Fusion, Title = PartTitle(module) };
            bool enabled = (bool)fields.Enabled.GetValue(module);
            bool charging = fields.Charging != null && (bool)fields.Charging.GetValue(module);
            info.State = enabled ? ReactorState.On : charging ? ReactorState.Charging : ReactorState.Off;
            if (fields.SystemPower != null) info.Heat = (float)fields.SystemPower.GetValue(module);
            if (fields.PowerProduced != null) info.Power = (float)fields.PowerProduced.GetValue(module);
            if (fields.OutletTemperature != null) info.CoreTemperature = (float)fields.OutletTemperature.GetValue(module);
            if (fields.CurrentCharge != null && fields.ChargeGoal != null)
            {
                float goal = (float)fields.ChargeGoal.GetValue(module);
                if (goal > 0f) info.ChargeFraction = (float)fields.CurrentCharge.GetValue(module) / goal;
            }
            return info;
        }
    }
}
