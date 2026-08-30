namespace MFDExtension.Cas
{
    // MAS companion module for the CAS bay's textmethod bridge - registered
    // as a sibling MODULE on MAS_JSI_BasicMFD (Config/Additive/MAS_BasicMFD.cfg
    // "5. CAS bay"), same "manual companion MODULE" pattern already used for
    // NavInstruments' KSF_MLS and VesselViewRPM's InternalVesselView (both
    // real examples read in this same install before writing this).
    //
    // Signature verified against MAS's real source (Source/MASPageText.cs,
    // 2026-08-19, not the wiki - too thin on detail): a textmethod target
    // must be `string Method(int, int)` (screenWidth, screenHeight),
    // resolved by matching ClassName among the prop's own internalModules -
    // so this MUST be an InternalModule (not a plain PartModule) for MAS to
    // find it at all.
    public class MFDExtCasModule : InternalModule
    {
        public string GetPageText(int screenWidth, int screenHeight)
        {
            Vessel vessel = (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null;
            return CasAggregator.BuildPage(vessel, screenWidth, screenHeight);
        }
    }
}
