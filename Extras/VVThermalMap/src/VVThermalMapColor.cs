using UnityEngine;

namespace VVThermalMap
{
    // Continuous blue/cyan/green/yellow/red heatmap of a part's shell
    // temperature: a direct temperature -> color function, independent of
    // VVEFISSeverity (no Tier, no alarm, no arbitration).
    //
    // Two regimes: an ABSOLUTE ambient ramp (3 K -> 300 K), the same for every
    // part whatever its skinMaxTemp, peaking at a single point of full green;
    // above it a RELATIVE danger curve on the part's own maximum. A flat
    // plateau relative to skinMaxTemp would run to absurd Kelvin values -
    // stock skins tolerate far more than their core (mk1Pod: 2200 vs 1200 K).
    internal static class VVThermalMapColor
    {
        internal const double FloorTemp = 3.0;
        internal const double PlateauLow = 250.0;
        internal const double GreenPeak = 300.0;

        // Admissibility floor for the core-override check only (see GetColor),
        // not a hue breakpoint - that is DangerHue's job.
        internal const double ShellWarnRatio = 0.6;

        // Calibrated so a representative part reaches a vivid orange right at
        // fraction 0.7, where the stock temperature gauge appears. With a fixed
        // ambient anchor and skinMaxTemp varying 800-3500 K across real parts,
        // that hue shifts slightly part to part - expected.
        private const float DangerCurveExponent = 0.64f;

        private const float HueBlue = 240f;
        private const float HueCyan = 180f;
        private const float HueGreen = 120f;
        private const float HueRed = 0f;

        internal static Color GetColor(Part part)
        {
            double skinFrac = part.skinTemperature / part.skinMaxTemp;
            double coreFrac = part.temperature / part.maxTemp;

            // The core only competes once it is itself past ShellWarnRatio: its
            // ordinary resting state stays below that floor, so it never
            // displaces the skin's cold-end reading. Above it, the worse of the
            // two wins - an engine cooking inside a skin that still reads fine.
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

        // Continuous green -> red curve above the ambient peak, shared by the
        // skin and by an overriding core reading. t is renormalized so 0 sits
        // at GreenPeak's fraction of THIS channel's own max and 1 at the max,
        // which also makes the junction with ShellHue's ramp seamless.
        private static float DangerHue(double frac, double maxTemp)
        {
            double ambientFrac = GreenPeak / maxTemp;
            float t = Mathf.Clamp01((float)((frac - ambientFrac) / (1.0 - ambientFrac)));
            float eased = Mathf.Pow(t, DangerCurveExponent);
            return Mathf.Lerp(HueGreen, HueRed, eased);
        }
    }
}
