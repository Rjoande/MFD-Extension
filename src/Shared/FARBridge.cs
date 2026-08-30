using System;
using System.Collections.Generic;
using System.Reflection;

namespace MFDExtension.Shared
{
    // THE single FAR stall reader for this whole project (2026-08-24
    // "shared basket" refactor) - previously two verbatim copies (src/Cas/
    // and Extras/VVEFIS/src/) plus the threshold constant duplicated a
    // third and fourth time in their consumers. Source-linked into both
    // DLLs, see DangItBridge.cs header for the mechanism and rationale.
    //
    // FAR (Ferram Aerospace Research) is optional and its types aren't
    // referenced here - same reflection-based duck-typing approach as
    // DangItBridge, reading VV's own two candidate module names
    // ("FARControllableSurface" / "FARWingAerodynamicModel", same pair
    // VesselViewer.cs checks for its native STALL/DRAG/LIFT modes) and the
    // public "stall" field VV itself reads off them.
    //
    // PROOF OF CONCEPT, NOT VERIFIED IN GAME: this project has no FAR
    // installation to test against - the field name/range are inferred
    // solely from VesselViewer.cs's own usage (genFractColor(1f - stall)),
    // not confirmed against FAR's real source. Degrades to "no data"
    // silently if the field is missing or FAR isn't installed.
    internal static class FARBridge
    {
        // Above this stall fraction a part reads as WARNING on every
        // channel. Unverified like the rest of this bridge - tune once
        // testable. Lives here so both channels share one value instead of
        // the two separate constants they used to carry.
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
