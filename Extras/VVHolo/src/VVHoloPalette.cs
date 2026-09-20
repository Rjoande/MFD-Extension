using MFDExtension.Shared;
using UnityEngine;

namespace VVHolo
{
    // Sci-fi "holographic blueprint" palette. Deliberately binary, unlike
    // VVEFISSeverity's four distinct hues: everything below Caution reads as
    // the same nominal cobalt + light wireframe baseline, because this mode is
    // meant to read as a HUD rather than as a diagnostic readout.
    //
    // Reuses VVEFISSeverity.GetStatus for its Tier classification only, never
    // its colors - which is why VVHolo.csproj source-links the whole Shared/
    // basket instead of just ModPresence.cs as VVThermalMap does.
    internal static class VVHoloPalette
    {
        private static readonly Color CobaltFill = new Color(0x1B / 255f, 0x3A / 255f, 0x6B / 255f);
        private static readonly Color WireLight = new Color(0xB3 / 255f, 0xE5 / 255f, 0xFC / 255f);

        // The project's one shared alarm red, not a new hex of its own.
        private static readonly Color WarningRed = VVEFISSeverity.WarningColor;

        // Halved by hand, for both the WARNING wireframe and the dark end of
        // the border pulse. NOT VV's native "dull" toggle: that one is
        // mode-wide, not per part, so it would darken nominal parts too.
        private static readonly Color WarningRedDark = WarningRed * 0.5f;

        // Same 1 Hz sine cadence as VVEFISSeverity's border pulse.
        private const float BlinkHz = 1f;

        internal static Color FillColor(Part part)
        {
            return GetTier(part) == Tier.Warning ? WarningRed : CobaltFill;
        }

        internal static Color WireColor(Part part)
        {
            return GetTier(part) == Tier.Warning ? WarningRedDark : WireLight;
        }

        // Alpha 0 below Caution, which makes VesselViewer skip the draw call
        // outright. Caution and Warning pulse alike: this mode looks at Tier
        // only and ignores PartStatus.Alarm, so unlike VVEFISSeverity.BoxColor
        // every Caution part breathes, with no exceptions.
        internal static Color BoxColor(Part part)
        {
            Tier tier = GetTier(part);
            if (tier != Tier.Warning && tier != Tier.Caution)
            {
                return Color.clear;
            }

            float pulse = (Mathf.Sin(Time.time * 2f * Mathf.PI * BlinkHz) + 1f) * 0.5f;
            return Color.Lerp(WarningRedDark, WarningRed, pulse);
        }

        private static Tier GetTier(Part part)
        {
            return VVEFISSeverity.GetStatus(part).Tier;
        }
    }
}
