namespace MFDExtension.Shared
{
    // The one severity scale shared by every channel that reports vessel
    // health in this project (2026-08-24 "shared basket" refactor, user
    // approved): VVEFIS maps a tier to a fill color on VesselView's 3D
    // schematic, the CAS bay maps it to a text section header - same
    // hierarchy, two presentations. Real-world grounding: FAA AC 25-11 /
    // EASA / MIL-STD-1472H color/severity conventions (researched by the
    // user before the palette was designed).
    //
    // Ordinal order IS the severity order - callers compare tiers with
    // plain < / > (see VVEFISSeverity.GetStatus's "highest tier wins").
    // CAS simply never emits Indication (an alerting page lists anomalies
    // only); that's a channel policy, not a different scale.
    internal enum Tier
    {
        Indication,
        Advisory,
        Caution,
        Warning
    }
}
