using MFDExtension.Shared;
using UnityEngine;

namespace VVHolo
{
    // Sci-fi "holographic blueprint" palette (2026-09-15, design closed in
    // chat before any code was written). Deliberately binary, unlike
    // VVEFISSeverity's own four-tier FillColor/BoxColor (WARNING/CAUTION/
    // ADVISORY/INDICATION, each a distinct hue): here every Advisory/
    // Indication/no-signal part reads identically as the nominal cobalt
    // fill + light wireframe baseline - confirmed with the user, this mode
    // is meant to read as a HUD, not a diagnostic readout, so it throws
    // away everything below Caution rather than encoding it.
    //
    // Reuses VVEFISSeverity.GetStatus purely for its Tier classification -
    // the same WARNING/CAUTION arbitration across DangIt/FAR/RealBattery/
    // fuel/engine already computed there - never its colors. That's why
    // VVHolo.csproj source-links the whole Shared/ basket (DangItBridge/
    // FARBridge/RealBatteryBridge/FuelEngineReader/Tier), not just
    // ModPresence.cs the way VVThermalMap does: VVThermalMap has no
    // severity logic of its own, this one leans on VVEFISSeverity's.
    internal static class VVHoloPalette
    {
        private static readonly Color CobaltFill = new Color(0x1B / 255f, 0x3A / 255f, 0x6B / 255f);
        private static readonly Color WireLight = new Color(0xB3 / 255f, 0xE5 / 255f, 0xFC / 255f);

        // Reuses the one shared alarm red (VVEFISSeverity.WarningColor)
        // instead of a new hex - stays recognizably "the project's red"
        // across every mode instead of introducing a slightly different one.
        private static readonly Color WarningRed = VVEFISSeverity.WarningColor;

        // Halved (user's explicit call), shared by two different uses: (1)
        // the WARNING wireframe's flat dark red - NOT VV's native per-mode
        // "dull" toggle (wireColorDullDelegate is a single mode-wide flag,
        // not per-part, so turning it on would darken every part's
        // wireframe including nominal cobalt ones, not just WARNING parts);
        // (2) the dark end of the border's breathing pulse below.
        private static readonly Color WarningRedDark = WarningRed * 0.5f;

        // Same cadence as VVEFISSeverity's own border pulse (1 Hz sine,
        // inside the real Master Caution band) - not revisited here, no
        // request to change it.
        private const float BlinkHz = 1f;

        internal static Color FillColor(Part part)
        {
            return GetTier(part) == Tier.Warning ? WarningRed : CobaltFill;
        }

        internal static Color WireColor(Part part)
        {
            return GetTier(part) == Tier.Warning ? WarningRedDark : WireLight;
        }

        // Transparent baseline (Color.clear, alpha 0 - VesselViewer skips
        // the draw call outright on alpha 0, see renderRects/GetBoxColor)
        // for every part below Caution. Caution and Warning both pulse
        // identically between WarningRedDark and WarningRed - confirmed
        // with the user (point D): this mode looks only at Tier, ignoring
        // PartStatus.Alarm entirely. Unlike VVEFISSeverity.BoxColor, where
        // Yellow-Caution states (SC_SOC/FUEL/FlamedOut) opt out of the
        // border via Alarm=false, here every Caution-tier part breathes -
        // this HUD doesn't make that distinction.
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
