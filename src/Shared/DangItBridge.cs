using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MFDExtension.Shared
{
    // THE single DangIt reader for this whole project (2026-08-24 "shared
    // basket" refactor) - previously two hand-synced copies (src/Cas/ and
    // Extras/VVEFIS/src/) that had already diverged twice: CAS grew
    // CollectFailures+ScreenName (log 42) and the unrecognized-Priority
    // default (log 43) while VVEFIS kept neither. This file is compiled
    // into BOTH DLLs by source-linking (each csproj includes it), so there
    // is no third runtime DLL and no new load-order dependency - CAS stays
    // VesselView-free, VVEFIS stays promotable to a standalone mod.
    //
    // DangIt exposes zero RPM/MAS/public API - but its FailureModule base
    // class (Failure_modules/FailureModule.cs in DangIt's own source) has
    // plain PUBLIC members we can read: HasFailed (bool), Priority (string,
    // "HIGH"/"MEDIUM"/"LOW" - a severity concept DangIt already has,
    // verified against every ModuleManager patch that sets it),
    // alarmDisabled (bool, DangIt's own player-acknowledgement flag), and
    // ScreenName (get-only string property, "Alternator"/"Gimbal"...).
    // Read by duck-typed reflection, not a hard reference to
    // DangItContinued.dll - the project-wide "no compile-time dependency on
    // an optional mod" convention. Degrades to "no failure" silently if
    // DangIt isn't installed, or if a future DangIt renames these members.
    internal static class DangItBridge
    {
        internal readonly struct FailureInfo
        {
            internal readonly string Priority; // "HIGH" / "MEDIUM" / "LOW" as set by DangIt's cfgs; the C# default is the raw tag "#LOC_DangIt_68" (= MEDIUM in en-us), and a localized install may carry a translated value - callers must NOT drop an unrecognized string, see MapPriorityToTier
            internal readonly bool Acknowledged; // DangIt's own alarmDisabled on this module
            internal readonly string Name; // DangIt's ScreenName property ("Alternator", "Gimbal"...), null if unreadable

            internal FailureInfo(string priority, bool acknowledged, string name)
            {
                Priority = priority;
                Acknowledged = acknowledged;
                Name = name;
            }
        }

        private struct FailureFields
        {
            internal FieldInfo HasFailed;
            internal FieldInfo Priority;
            internal FieldInfo AlarmDisabled;
            internal PropertyInfo ScreenName; // abstract get-only PROPERTY on FailureModule, not a field - may be null if a future DangIt drops it (tolerated: Name comes back null)
        }

        // Per-Type field-lookup cache - avoids repeating GetField() for every
        // part on every render pass. null entry = "this module type isn't a
        // DangIt failure module", cached too so we don't re-check every time.
        private static readonly Dictionary<Type, FailureFields?> fieldCache = new Dictionary<Type, FailureFields?>();

        // Maps DangIt's Priority string to the shared severity scale. The
        // default branch is deliberate and load-bearing (log 43): DangIt's
        // own C# default for Priority is the raw localization tag
        // "#LOC_DangIt_68" (= MEDIUM in en-us, verified in the local DangIt
        // source and Localization/en-us.cfg), and a localized install
        // carries a translated string entirely. A severity channel must
        // never silently drop a real failure over a label it doesn't
        // recognize - anything not clearly HIGH or LOW maps to Caution,
        // matching what that default means. (Unifying this closed a real
        // gap: the old VVEFIS copy returned "no status" for unrecognized
        // strings, hiding the failure from the 3D view.)
        internal static Tier MapPriorityToTier(string priority)
        {
            switch (priority)
            {
                case "HIGH": return Tier.Warning;
                case "LOW": return Tier.Advisory;
                default: return Tier.Caution;
            }
        }

        // Appends one FailureInfo per FAILED DangIt module on this part - a
        // part can carry more than one failure module (e.g. an engine has both
        // its own failure and, separately, a gimbal failure), each capable of
        // failing independently. CAS lists each as its own alert line
        // (first in-game test 2026-08-23: worst-wins-per-part undercounted
        // exactly this case); VVEFIS reduces the list to the worst via
        // TryGetWorstFailure below (one part = one fill color).
        internal static void CollectFailures(Part part, List<FailureInfo> results)
        {
            foreach (PartModule module in part.Modules)
            {
                if (module == null) continue;

                FailureFields? fields = GetFailureFields(module.GetType());
                if (fields == null) continue;

                bool hasFailed;
                try
                {
                    hasFailed = (bool)fields.Value.HasFailed.GetValue(module);
                }
                catch
                {
                    continue;
                }
                if (!hasFailed) continue;

                string priority;
                bool acknowledged;
                try
                {
                    priority = (string)fields.Value.Priority.GetValue(module);
                    acknowledged = (bool)fields.Value.AlarmDisabled.GetValue(module);
                }
                catch
                {
                    continue;
                }

                string name = null;
                if (fields.Value.ScreenName != null)
                {
                    try
                    {
                        name = fields.Value.ScreenName.GetValue(module, null) as string;
                    }
                    catch
                    {
                        // a throwing getter must not hide the failure itself - keep the entry, nameless
                    }
                }

                results.Add(new FailureInfo(priority, acknowledged, name));
            }
        }

        // Reused across frames by TryGetWorstFailure - single-threaded like
        // everything else on the prop, keep per-call garbage down.
        private static readonly List<FailureInfo> worstBuffer = new List<FailureInfo>();

        // The worst-wins reduction VVEFIS needs (one part = one fill color):
        // highest tier wins, ties broken by module order on the part.
        internal static bool TryGetWorstFailure(Part part, out FailureInfo worst, out Tier tier)
        {
            worstBuffer.Clear();
            CollectFailures(part, worstBuffer);

            worst = default;
            tier = Tier.Indication;
            if (worstBuffer.Count == 0) return false;

            bool first = true;
            foreach (FailureInfo failure in worstBuffer)
            {
                Tier candidate = MapPriorityToTier(failure.Priority);
                if (first || candidate > tier)
                {
                    worst = failure;
                    tier = candidate;
                    first = false;
                }
            }
            return true;
        }

        // CAS mute-all (2026-08-30, RPM_MODULE/ButtonProcessor bridge - see
        // src/Cas/MFDExtCasModule.cs): mirrors DangIt's own "Mute All" GUI
        // button (Runtime/GUI/FailureStatusWindow.cs), which calls this
        // exact method - AlarmManager.RemoveAllAlarms(), found via
        // FindObjectOfType, same pattern DangIt itself uses everywhere to
        // reach its own singleton. NOT a loop over FailureModule.MuteAlarms()
        // (that mutes one module at a time, the part right-click action) -
        // RemoveAllAlarms() clears every queued alarm in one call, silencing
        // the sound only; it does not touch FailureState/ScreenName, so
        // CAS's own list is unaffected (signal vs information, confirmed
        // with the user 2026-08-30).
        //
        // Unlike CollectFailures above, there's no PartModule instance to
        // read GetType() from - AlarmManager is a KSPAddon MonoBehaviour
        // singleton, so its Type has to be found by searching the DangIt
        // assembly itself. Matched by simple class name only (not a
        // namespace-qualified "nsDangIt.AlarmManager") to tolerate a fork
        // (DangItContinued) using a different namespace - same
        // fork-tolerance philosophy as ModPresence's multi-candidate CLR
        // names.
        private static bool alarmManagerResolved;
        private static Type alarmManagerType;
        private static MethodInfo removeAllAlarmsMethod;

        internal static void MuteAllAlarms()
        {
            if (!alarmManagerResolved)
            {
                alarmManagerResolved = true;
                ResolveAlarmManager();
            }

            if (alarmManagerType == null || removeAllAlarmsMethod == null) return;

            UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(alarmManagerType);
            if (instance == null) return;

            try
            {
                removeAllAlarmsMethod.Invoke(instance, null);
            }
            catch
            {
                // a throwing third-party method must not break our own button handling
            }
        }

        private static void ResolveAlarmManager()
        {
            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                string name = loaded.assembly.GetName().Name;
                if (name != "DangIt" && name != "DangItContinued") continue;

                Type[] types;
                try
                {
                    types = loaded.assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types; // some types can fail to load in a mod assembly; salvage the rest
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type != null && type.Name == "AlarmManager")
                    {
                        alarmManagerType = type;
                        removeAllAlarmsMethod = type.GetMethod("RemoveAllAlarms", BindingFlags.Public | BindingFlags.Instance);
                        return;
                    }
                }
            }
        }

        private static FailureFields? GetFailureFields(Type moduleType)
        {
            if (fieldCache.TryGetValue(moduleType, out FailureFields? cached))
            {
                return cached;
            }

            FieldInfo hasFailed = moduleType.GetField("HasFailed", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo priority = moduleType.GetField("Priority", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo alarmDisabled = moduleType.GetField("alarmDisabled", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo screenName = moduleType.GetProperty("ScreenName", BindingFlags.Public | BindingFlags.Instance);

            FailureFields? result = null;
            if (hasFailed != null && hasFailed.FieldType == typeof(bool)
                && priority != null && priority.FieldType == typeof(string)
                && alarmDisabled != null && alarmDisabled.FieldType == typeof(bool))
            {
                if (screenName != null && (screenName.PropertyType != typeof(string) || !screenName.CanRead))
                {
                    screenName = null;
                }
                result = new FailureFields { HasFailed = hasFailed, Priority = priority, AlarmDisabled = alarmDisabled, ScreenName = screenName };
            }

            fieldCache[moduleType] = result;
            return result;
        }
    }
}
