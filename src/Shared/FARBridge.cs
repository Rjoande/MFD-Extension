using System;
using System.Collections.Generic;
using System.Reflection;

namespace MFDExtension.Shared
{
    // The FAR stall reader for the whole project, duck-typed by reflection
    // like DangItBridge: the two candidate module names and the public
    // "stall" field are the ones VesselViewer itself reads.
    //
    // PROOF OF CONCEPT, NEVER TESTED IN GAME: there is no FAR install here,
    // and the field name and range are inferred from VesselViewer's usage
    // alone. Degrades silently to "no data".
    internal static class FARBridge
    {
        // Above this stall fraction a part reads WARNING on every channel.
        // Unverified like the rest of this bridge; tune once testable.
        internal const float StallWarningThreshold = 0.7f;

        private static readonly string[] CandidateModuleNames = { "FARControllableSurface", "FARWingAerodynamicModel" };

        private static readonly Dictionary<Type, FieldInfo> fieldCache = new Dictionary<Type, FieldInfo>();

        internal static bool TryGetStall(Part part, out float stall)
        {
            stall = 0f;

            foreach (string moduleName in CandidateModuleNames)
            {
                if (!part.Modules.Contains(moduleName)) continue;

                PartModule module = part.Modules[moduleName];
                FieldInfo field = GetStallField(module.GetType());
                if (field == null) continue;

                try
                {
                    stall = (float)field.GetValue(module);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static FieldInfo GetStallField(Type moduleType)
        {
            if (fieldCache.TryGetValue(moduleType, out FieldInfo cached))
            {
                return cached;
            }

            FieldInfo field = moduleType.GetField("stall", BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType != typeof(float))
            {
                field = null;
            }

            fieldCache[moduleType] = field;
            return field;
        }
    }
}
