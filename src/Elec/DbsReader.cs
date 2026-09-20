using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace MFDExtension.Elec
{
    // One part's contribution to the electrical ledger, as DynamicBatteryStorage
    // (DBS) estimates it: Value > 0 feeds the bus, < 0 draws from it [Ec/s].
    internal struct ElecEntry
    {
        public string Title;
        public double Value;
    }

    // One DBS UI category ("Solar Panels", "Engines"...) plus our own OTHER
    // bucket. Sources and Loads are split by the SIGN of the value read in
    // this snapshot, not by DBS's Producer/Consumer flags: several handlers
    // flip those at runtime (RealBatteryPowerHandler does on every read), the
    // sign is what the number on screen actually means.
    internal sealed class ElecCategory
    {
        public string Key;        // DBS's cfg `name` ("Batteries"), or DbsReader.OtherKey
        public string Title;      // DBS's own localized title, uppercased
        public bool IsStorage;    // the "Batteries" category: RealBattery + DischargeCapacitor
        public readonly List<ElecEntry> Sources = new List<ElecEntry>();
        public readonly List<ElecEntry> Loads = new List<ElecEntry>();
        public double SourceSum;  // >= 0
        public double LoadSum;    // <= 0
        public int IdleSources;   // handlers reading ~0, sided by DBS's Producer flag
        public int IdleLoads;
    }

    internal enum DbsBufferState
    {
        Unknown,      // controller module not found on this vessel
        Disabled,     // Settings.Enabled == false (DBS switches itself off next to Kerbalism)
        Off,          // warp below Settings.TimeWarpLimit
        Active,       // analytic mode: the buffer is holding the vessel's EC up
    }

    internal sealed class ElecSnapshot
    {
        public bool HasData;      // false: DBS present but its vessel data isn't built yet
        public readonly List<ElecCategory> Categories = new List<ElecCategory>();
        public double Ec;
        public double MaxEc;
        public DbsBufferState Buffer;
        public double BufferSize;
        public float TimeWarpLimit = 100f;
    }

    // The DynamicBatteryStorage reader. Every member below was read on the
    // decompiled 2.3.7 assembly and cross-checked against 2.3.3. Reflection
    // only, no compile-time dependency on an optional mod; every failure
    // degrades to "no data" and never throws into MAS.
    //
    // Lives in src/Elec/, NOT src/Shared/: the satellite DLLs (VVEFIS, VVHolo)
    // glob Shared/*.cs and none of them reads DBS.
    //
    // Three facts that shaped this file, all verified on the source:
    //  - VesselDataManager.ElectricalData is REPLACED by a new object on every
    //    onVesselWasModified / onVesselGoOnRails - never cache the instance,
    //    only the PropertyInfo that fetches it.
    //  - VesselData.CurrentProduction and .CurrentConsumption each call
    //    GetValue() on every handler; reading both plus the list would cost
    //    three GetValue() per handler. One pass here yields the same totals
    //    (same definition: sum of positives / sum of negatives) with one.
    //  - Settings.* and UIHandlerCategory.* are FIELDS; VesselData and
    //    ModuleDataHandler members are PROPERTIES.
    internal static class DbsReader
    {
        internal const string AssemblyName = "DynamicBatteryStorage";
        internal const string StorageCategoryKey = "Batteries"; // cfg `name`, not the localized title
        internal const string OtherKey = "__other";
        internal const double IdleThreshold = 0.005;            // below this a value prints as 0.00

        private static bool resolved;
        private static bool available;

        private static Type managerType;
        private static PropertyInfo managerElectricalData;
        private static PropertyInfo managerReady;
        private static PropertyInfo dataAllHandlers;
        private static PropertyInfo handlerPM;
        private static PropertyInfo handlerProducer;
        private static MethodInfo handlerGetValue;
        private static MethodInfo handlerPartTitle;

        private static Type controllerType;
        private static PropertyInfo controllerAnalyticMode;
        private static PropertyInfo controllerBufferSize;

        private static FieldInfo settingsEnabled;
        private static FieldInfo settingsTimeWarpLimit;
        private static FieldInfo settingsCategoryData;
        private static FieldInfo categoryName;
        private static FieldInfo categoryTitle;
        private static FieldInfo categoryModules;

        // moduleName -> category key, built once DBS has loaded its cfg
        // (Settings.HandlerCategoryData is null until then). First category
        // listing a module wins; DBS's own UI would list it under each.
        private static Dictionary<string, string> moduleToCategory;
        private static readonly Dictionary<string, string> categoryTitles = new Dictionary<string, string>();

        private static readonly Dictionary<string, ElecCategory> categoryIndex = new Dictionary<string, ElecCategory>();

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
                // CLR name, never LoadedAssembly.name - see Shared/ModPresence.
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

                managerType = assembly.GetType("DynamicBatteryStorage.VesselDataManager");
                Type dataType = assembly.GetType("DynamicBatteryStorage.VesselData");
                Type handlerType = assembly.GetType("DynamicBatteryStorage.ModuleDataHandler");
                Type settingsType = assembly.GetType("DynamicBatteryStorage.Settings");
                Type categoryType = assembly.GetType("DynamicBatteryStorage.UIHandlerCategory");
                controllerType = assembly.GetType("DynamicBatteryStorage.ModuleDynamicBatteryStorage");
                if (managerType == null || dataType == null || handlerType == null) return;

                managerElectricalData = managerType.GetProperty("ElectricalData", inst);
                managerReady = managerType.GetProperty("Ready", inst);
                dataAllHandlers = dataType.GetProperty("AllHandlers", inst);
                handlerPM = handlerType.GetProperty("PM", inst);
                handlerProducer = handlerType.GetProperty("Producer", inst);
                handlerGetValue = handlerType.GetMethod("GetValue", inst, null, Type.EmptyTypes, null);
                handlerPartTitle = handlerType.GetMethod("PartTitle", inst, null, Type.EmptyTypes, null);
                if (managerElectricalData == null || dataAllHandlers == null || handlerPM == null || handlerGetValue == null) return;

                // Everything below is optional: a missing member costs one
                // row of the page (BUFFER) or the category names (everything
                // lands in OTHER), never the ledger itself.
                if (controllerType != null)
                {
                    controllerAnalyticMode = controllerType.GetProperty("AnalyticMode", inst);
                    controllerBufferSize = controllerType.GetProperty("BufferSize", inst);
                }
                if (settingsType != null)
                {
                    settingsEnabled = settingsType.GetField("Enabled", stat);
                    settingsTimeWarpLimit = settingsType.GetField("TimeWarpLimit", stat);
                    settingsCategoryData = settingsType.GetField("HandlerCategoryData", stat);
                }
                if (categoryType != null)
                {
                    categoryName = categoryType.GetField("name", inst);
                    categoryTitle = categoryType.GetField("title", inst);
                    categoryModules = categoryType.GetField("handledModules", inst);
                }

                available = true;
            }
            catch
            {
                available = false;
            }
        }

        private static void EnsureCategoryMap()
        {
            if (moduleToCategory != null) return;
            if (settingsCategoryData == null || categoryName == null || categoryModules == null) return;

            IDictionary raw;
            try
            {
                raw = settingsCategoryData.GetValue(null) as IDictionary;
            }
            catch
            {
                return;
            }
            if (raw == null || raw.Count == 0) return; // DBS hasn't loaded its cfg yet - retry on the next snapshot

            Dictionary<string, string> map = new Dictionary<string, string>();
            categoryTitles.Clear();
            foreach (DictionaryEntry pair in raw)
            {
                object category = pair.Value;
                if (category == null) continue;
                try
                {
                    string key = categoryName.GetValue(category) as string;
                    if (string.IsNullOrEmpty(key)) continue;
                    string title = categoryTitle != null ? categoryTitle.GetValue(category) as string : null;
                    if (string.IsNullOrEmpty(title)) title = key;
                    categoryTitles[key] = title.ToUpperInvariant();

                    IEnumerable modules = categoryModules.GetValue(category) as IEnumerable;
                    if (modules == null) continue;
                    foreach (object module in modules)
                    {
                        string moduleName = module as string;
                        if (!string.IsNullOrEmpty(moduleName) && !map.ContainsKey(moduleName)) map[moduleName] = key;
                    }
                }
                catch
                {
                    // one unreadable category must not cost the others
                }
            }
            moduleToCategory = map;
        }

        private static ElecCategory GetCategory(ElecSnapshot snapshot, string key)
        {
            ElecCategory category;
            if (categoryIndex.TryGetValue(key, out category)) return category;

            string title;
            if (key == OtherKey) title = "OTHER";
            else if (!categoryTitles.TryGetValue(key, out title)) title = key.ToUpperInvariant();

            category = new ElecCategory { Key = key, Title = title, IsStorage = key == StorageCategoryKey };
            categoryIndex[key] = category;
            snapshot.Categories.Add(category);
            return category;
        }

        // Fills `snapshot` from scratch. Returns false (snapshot.HasData
        // false) when DBS is installed but has nothing for this vessel yet:
        // the manager only builds its data once the vessel is the active one.
        internal static bool Read(Vessel vessel, ElecSnapshot snapshot)
        {
            snapshot.HasData = false;
            snapshot.Categories.Clear();
            categoryIndex.Clear();
            snapshot.Ec = 0.0;
            snapshot.MaxEc = 0.0;
            snapshot.Buffer = DbsBufferState.Unknown;
            snapshot.BufferSize = 0.0;

            Resolve();
            if (!available || vessel == null) return false;

            object manager = null;
            object controller = null;
            List<VesselModule> vesselModules = vessel.vesselModules;
            if (vesselModules == null) return false;
            for (int i = 0; i < vesselModules.Count; ++i)
            {
                VesselModule vesselModule = vesselModules[i];
                if (vesselModule == null) continue;
                if (manager == null && managerType.IsInstanceOfType(vesselModule)) manager = vesselModule;
                else if (controller == null && controllerType != null && controllerType.IsInstanceOfType(vesselModule)) controller = vesselModule;
            }
            if (manager == null) return false;

            IList handlers;
            try
            {
                if (managerReady != null && !(bool)managerReady.GetValue(manager, null)) return false;
                object data = managerElectricalData.GetValue(manager, null); // re-fetched every time, see the class comment
                if (data == null) return false;
                handlers = dataAllHandlers.GetValue(data, null) as IList;
            }
            catch
            {
                return false;
            }
            if (handlers == null) return false;

            EnsureCategoryMap();

            for (int i = 0; i < handlers.Count; ++i)
            {
                object handler = handlers[i];
                if (handler == null) continue;

                PartModule module;
                double value;
                try
                {
                    module = handlerPM.GetValue(handler, null) as PartModule;
                    if (module == null || module.part == null) continue; // DBS prunes these itself on its next refresh
                    value = (double)handlerGetValue.Invoke(handler, null);
                }
                catch
                {
                    continue;
                }
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;

                string key;
                if (moduleToCategory == null || !moduleToCategory.TryGetValue(module.moduleName, out key)) key = OtherKey;
                ElecCategory category = GetCategory(snapshot, key);

                if (Math.Abs(value) < IdleThreshold)
                {
                    bool producer = false;
                    try
                    {
                        if (handlerProducer != null) producer = (bool)handlerProducer.GetValue(handler, null);
                    }
                    catch
                    {
                        // sided as a load - only affects which page counts it as idle
                    }
                    if (producer) category.IdleSources++; else category.IdleLoads++;
                    continue;
                }

                string title = null;
                try
                {
                    if (handlerPartTitle != null) title = handlerPartTitle.Invoke(handler, null) as string;
                }
                catch
                {
                    // fall through to the stock title below
                }
                if (string.IsNullOrEmpty(title)) title = module.part.partInfo != null ? module.part.partInfo.title : module.part.name;

                ElecEntry entry = new ElecEntry { Title = title, Value = value };
                if (value > 0.0)
                {
                    category.Sources.Add(entry);
                    category.SourceSum += value;
                }
                else
                {
                    category.Loads.Add(entry);
                    category.LoadSum += value;
                }
            }

            for (int i = 0; i < snapshot.Categories.Count; ++i)
            {
                snapshot.Categories[i].Sources.Sort(CompareByMagnitude);
                snapshot.Categories[i].Loads.Sort(CompareByMagnitude);
            }

            // Same call DBS's own UI makes (VesselElectricalData.
            // GetElectricalChargeLevels), on OUR vessel instead of
            // FlightGlobals.ActiveVessel - identical in IVA.
            try
            {
                double ec, maxEc;
                vessel.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out ec, out maxEc, true);
                snapshot.Ec = ec;
                snapshot.MaxEc = maxEc;
            }
            catch
            {
                // EC LEVEL degrades to "no storage"
            }

            ReadBuffer(controller, snapshot);

            snapshot.HasData = true;
            return true;
        }

        private static void ReadBuffer(object controller, ElecSnapshot snapshot)
        {
            try
            {
                if (settingsTimeWarpLimit != null) snapshot.TimeWarpLimit = (float)settingsTimeWarpLimit.GetValue(null);
                if (settingsEnabled != null && !(bool)settingsEnabled.GetValue(null))
                {
                    snapshot.Buffer = DbsBufferState.Disabled;
                    return;
                }
                if (controller == null || controllerAnalyticMode == null) return; // stays Unknown

                bool analytic = (bool)controllerAnalyticMode.GetValue(controller, null);
                snapshot.Buffer = analytic ? DbsBufferState.Active : DbsBufferState.Off;
                if (analytic && controllerBufferSize != null) snapshot.BufferSize = (double)controllerBufferSize.GetValue(controller, null);
            }
            catch
            {
                snapshot.Buffer = DbsBufferState.Unknown;
            }
        }

        private static int CompareByMagnitude(ElecEntry a, ElecEntry b)
        {
            int byValue = Math.Abs(b.Value).CompareTo(Math.Abs(a.Value));
            return byValue != 0 ? byValue : string.CompareOrdinal(a.Title, b.Title);
        }
    }
}
