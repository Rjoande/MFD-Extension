using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MFDExtension.Shared
{
    // The DangIt reader for the whole project, source-linked into every DLL
    // that needs it rather than duplicated (the two old copies had drifted).
    //
    // DangIt exposes no public API, but its FailureModule base class has plain
    // public members: HasFailed, Priority, alarmDisabled and the ScreenName
    // property. Read by duck-typed reflection, no compile-time dependency on
    // an optional mod; degrades silently to "no failure".
    internal static class DangItBridge
    {
        internal readonly struct FailureInfo
        {
            // "HIGH"/"MEDIUM"/"LOW" as set by DangIt's cfgs, but its own C#
            // default is a raw #LOC tag and a localized install carries a
            // translated string - see MapPriorityToTier, never drop one.
            internal readonly string Priority;
            internal readonly bool Acknowledged; // DangIt's own alarmDisabled on this module
            internal readonly string Name; // ScreenName ("Alternator", "Gimbal"...), null if unreadable

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
            internal PropertyInfo ScreenName; // get-only PROPERTY, not a field; null tolerated (Name comes back null)
        }

        // Per-Type lookups, cached across render passes; a null entry means
        // "not a DangIt failure module" and is cached too.
        private static readonly Dictionary<Type, FailureFields?> fieldCache = new Dictionary<Type, FailureFields?>();

        // The default branch is load-bearing: anything not clearly HIGH or LOW
        // maps to Caution instead of being dropped, because DangIt's own C#
        // default is a raw #LOC tag (MEDIUM) and localized installs differ.
        internal static Tier MapPriorityToTier(string priority)
        {
            switch (priority)
            {
                case "HIGH": return Tier.Warning;
                case "LOW": return Tier.Advisory;
                default: return Tier.Caution;
            }
        }

        // One FailureInfo per FAILED module on this part: a part can carry
        // several failure modules failing independently (an engine has its own
        // plus a gimbal one). CAS lists each, VVEFIS reduces to the worst.
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

        // Reused across frames; everything on the prop is single-threaded.
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

        // Mirrors DangIt's own "Mute All" GUI button, which calls this same
        // AlarmManager.RemoveAllAlarms(): it silences every queued alarm at
        // once and never touches failure state, so CAS's list is unaffected.
        //
        // No PartModule to take a Type from here - AlarmManager is a KSPAddon
        // singleton, so its Type is searched for in the assembly by simple
        // class name, which keeps a fork with its own namespace matching.
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
