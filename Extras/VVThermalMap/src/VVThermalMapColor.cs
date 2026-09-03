using UnityEngine;

namespace VVThermalMap
{
    // Continuous blue/cyan/green/yellow/red heatmap of a part's shell
    // temperature (design closed in chat, 2026-08-30 - see CLAUDE.md log
    // 1/3/6). Deliberately independent of VVEFISSeverity: no Tier/Alarm/
    // arbitration, just a direct temperature -> color function.
    //
    // The curve has two regimes:
    //  - an ABSOLUTE ambient segment (3 K -> 300 K) identical for every
    //    part, regardless of its own skinMaxTemp: blue->cyan->green ramp
    //    from the cosmic-background floor (also close to KSP's own
    //    Part.skinUnexposedExternalTemp default, verified on decompiled
    //    Part.cs) up to GreenPeak (300 K), a single POINT of full green,
    //    not a flat band - the user's call (2026-08-30 log 7): the
    //    273-373 K range stays close enough to uniform in practice that a
    //    band added nothing a single peak doesn't already give;
    //  - a RELATIVE danger curve above that (log 6, replaces the original
    //    flat-plateau three-segment design closed at log 1): a single
    //    continuous power curve, not a piecewise one, because the first
    //    in-game test showed the old flat plateau (up to ShellWarnRatio of
    //    skinMaxTemp) stretched to absurd Kelvin values on real stock parts -
    //    verified on actual .cfg files, e.g. mk1Pod_v2.cfg has
    //    skinMaxTemp=2200 while its own maxTemp is 1200 (deliberate stock
    //    design: a capsule's skin is a heat-shield layer, tolerating far
    //    more than its core) - 0.6 of 2200 is 1320 K, long after the player
    //    already sees reentry plasma and the stock gauge.
    internal static class VVThermalMapColor
    {
        internal const double FloorTemp = 3.0;
        internal const double PlateauLow = 250.0;
        internal const double GreenPeak = 300.0;

        // Admissibility floor for the core-override check only (see
        // GetColor) - NOT a hue breakpoint any more, that's DangerHue's job.
        internal const double ShellWarnRatio = 0.6;

        // t^DangerCurveExponent, t renormalized above GreenPeak (see
        // DangerHue) - calibrated so a representative part (mk1Pod_v2,
        // skinMaxTemp 2200 K) reaches a vivid orange (hue ~=30 deg) right at
        // frac=0.7, aligned with stock's own gaugeThreshold default
        // (TemperatureGauge.cs, verified log 1) - the point the user
        // confirmed as the target (log 6; still holds within about 1 deg
        // of hue after GreenPeak moved from 373 to 300 K at log 7, verified
        // on paper, not re-picked). Because the ambient anchor is a fixed
        // 300 K while skinMaxTemp varies a lot between real parts
        // (800-3500 K, verified on stock .cfgs), the exact hue at frac=0.7
        // shifts slightly part to part - expected, not something one
        // exponent is meant to erase.
        private const float DangerCurveExponent = 0.64f;

        private const float HueBlue = 240f;
        private const float HueCyan = 180f;
        private const float HueGreen = 120f;
        private const float HueRed = 0f;

        internal static Color GetColor(Part part)
        {
            double skinFrac = part.skinTemperature / part.skinMaxTemp;
            double coreFrac = part.temperature / part.maxTemp;

            // Generalist-sensor override (2026-08-30 log 3): the core only
            // ever competes once it is ITSELF past ShellWarnRatio - its
            // ordinary ~280-300 K resting state stays under that floor for
            // any realistic maxTemp, so it never displaces the skin's cold-end
            // reading. Above that floor, it wins if it is the worse of the
            // two - e.g. an engine cooking internally while its outer skin
            // still reads fine, the case a plain skin-only sensor would miss.
            bool coreOverrides = coreFrac > ShellWarnRatio && coreFrac > skinFrac;

            float hue = coreOverrides
                ? DangerHue(coreFrac, part.maxTemp)
                : ShellHue(part.skinTemperature, skinFrac, part.skinMaxTemp);

            return Color.HSVToRGB(hue / 360f, 1f, 1f);
        }

        private static float ShellHue(double skinTemperature, double skinFrac, double skinMaxTemp)
        {
            if (skinTemperature <= FloorTemp) return HueBlue;
            if (skinTemperature < PlateauLow)
                return Mathf.Lerp(HueBlue, HueCyan, (float)((skinTemperature - FloorTemp) / (PlateauLow - FloorTemp)));
            if (skinTemperature < GreenPeak)
                return Mathf.Lerp(HueCyan, HueGreen, (float)((skinTemperature - PlateauLow) / (GreenPeak - PlateauLow)));

            return DangerHue(skinFrac, skinMaxTemp);
        }

        // Continuous danger curve (green -> red) above the part's own
        // ambient peak, shared by the skin (above GreenPeak) and by an
        // overriding core reading alike. t is renormalized so 0 sits right
        // at GreenPeak's fraction of THIS channel's own max (not at 0) and
        // 1 sits at the max itself - the eased power curve then front-loads
        // the move away from green (see DangerCurveExponent). Continuous at
        // the boundary: at skinTemperature == GreenPeak, frac == ambientFrac
        // exactly, t == 0, hue == green - matches ShellHue's own ramp
        // reaching green at that same point, no seam.
        private static float DangerHue(double frac, double maxTemp)
        {
            double ambientFrac = GreenPeak / maxTemp;
            float t = Mathf.Clamp01((float)((frac - ambientFrac) / (1.0 - ambientFrac)));
            float eased = Mathf.Pow(t, DangerCurveExponent);
            return Mathf.Lerp(HueGreen, HueRed, eased);
        }
    }
}
