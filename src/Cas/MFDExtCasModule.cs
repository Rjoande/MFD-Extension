using MFDExtension.Shared;

namespace MFDExtension.Cas
{
    // MAS companion module for the CAS bay, registered as a sibling MODULE on
    // MAS_JSI_BasicMFD (Config/Additive/MAS_BasicMFD.cfg section 5) - the same
    // manual-companion pattern NavInstruments' KSF_MLS uses.
    //
    // A textmethod target must be `string Method(int, int)` (screenWidth,
    // screenHeight) and MAS resolves it among the prop's internalModules, so
    // this MUST be an InternalModule for MAS to find it at all.
    public class MFDExtCasModule : InternalModule
    {
        // Physical button ids, see MAS_JSI_BasicMFD.cfg's right-column comment.
        // Configurable from the cfg, defaulting to the real ids.
        [KSPField]
        public int buttonMute = 3;  // button_ESC, the red "x"
        [KSPField]
        public int buttonUp = 0;
        [KSPField]
        public int buttonDown = 1;
        [KSPField]
        public int buttonHome = 4;  // the white circle - jumps back to the top

        // Per-prop-instance state, like the button ids above: a second CAS
        // monitor on the same vessel scrolls independently.
        private int scrollOffset;

        public string GetPageText(int screenWidth, int screenHeight)
        {
            Vessel vessel = (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null;
            return CasAggregator.BuildPage(vessel, screenWidth, screenHeight, ref scrollOffset);
        }

        // The RPM_MODULE/buttonClickMethod bridge declared in MFDExt_CAS.cfg:
        // a second, Lua-free MAS extension point, called for every press this
        // page's own `softkey =` entries didn't claim. CAS declares none, so
        // this method alone decides what each buttonID does.
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
