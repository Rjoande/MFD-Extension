using MFDExtension.Shared;

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
        // Physical buttons this page's RPM_MODULE claims (see
        // MAS_JSI_BasicMFD.cfg's right-column comment for the id/symbol
        // mapping). Configurable via MAS_BasicMFD.cfg §5 like KSF_MLS's own
        // btnPrevRwy/btnNextRwy fields, defaulting to the real ids either way.
        [KSPField]
        public int buttonMute = 3;  // button_ESC, the red "x"
        [KSPField]
        public int buttonUp = 0;    // button_UP, "^"
        [KSPField]
        public int buttonDown = 1;  // button_DOWN, "v"
        [KSPField]
        public int buttonHome = 4;  // button_HOME, "<" - jumps the scroll back to the top

        // Index into the flat WARNING/CAUTION/ADVISORY entry sequence - see
        // CasAggregator.BuildPage. Lives here (not in CasAggregator) because
        // it's per-prop-instance state, exactly like buttonMute above; a
        // second CAS-bearing monitor on the same vessel scrolls independently.
        private int scrollOffset;

        public string GetPageText(int screenWidth, int screenHeight)
        {
            Vessel vessel = (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null;
            return CasAggregator.BuildPage(vessel, screenWidth, screenHeight, ref scrollOffset);
        }

        // RPM-legacy PAGEHANDLER button bridge (RPM_MODULE/buttonClickMethod
        // in Pages/MFDExt_CAS.cfg) - a second, Lua-free MAS extension point
        // distinct from `softkey = N, <lua>`, the same one NavInstruments'
        // KSF_MLS/ButtonProcessor and VesselViewRPM's InternalVesselView use
        // (real, local examples read before writing this; confirmed against
        // MAS's own MASPage.cs/MASPageRpmModule.cs source, 2026-08-30).
        // Called for every button press on this page not already claimed by
        // a page-level `softkey =` entry - CAS defines none, so this method
        // alone decides what (if anything) each buttonID does.
        public void ButtonProcessor(int buttonID)
        {
            if (buttonID == buttonMute)
            {
                DangItBridge.MuteAllAlarms();
            }
            else if (buttonID == buttonDown)
            {
                Vessel vessel = (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null;
                CasAggregator.TryScrollDown(vessel, ref scrollOffset);
            }
            else if (buttonID == buttonUp)
            {
                if (scrollOffset > 0) scrollOffset--;
            }
            else if (buttonID == buttonHome)
            {
                scrollOffset = 0;
            }
        }
    }
}
