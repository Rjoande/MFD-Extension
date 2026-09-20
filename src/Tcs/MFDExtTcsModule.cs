namespace MFDExtension.Tcs
{
    // MAS companion module for the TCS bay (R4, Thermal Control System) -
    // SystemHeat's loops, members and reactors on three pages. Registered as
    // a sibling MODULE on MAS_JSI_BasicMFD (Config/Additive/MAS_BasicMFD.cfg
    // section 8), same "manual companion MODULE" pattern as MFDExtCasModule
    // and MFDExtElecModule - read those files for why this must be an
    // InternalModule and for the textmethod / RPM_MODULE signatures.
    //
    // ONE module serves all three pages: each list page's TEXT names its own
    // textmethod and its RPM_MODULE its own buttonClickMethod, which is what
    // gives every page its own scroll offset. The summary page binds no key
    // at all and therefore carries no RPM_MODULE.
    public class MFDExtTcsModule : InternalModule
    {
        // Physical button ids, see MAS_JSI_BasicMFD.cfg's right-column
        // comment (UP 0, DOWN 1, HOME 4). Configurable from the cfg like
        // CAS's and ELEC's, defaulting to the real ids.
        [KSPField]
        public int buttonUp = 0;
        [KSPField]
        public int buttonDown = 1;
        [KSPField]
        public int buttonHome = 4;  // the white circle

        // Per-prop-instance state, not persisted.
        private int loopsOffset;
        private int reactorsOffset;

        private Vessel CurrentVessel
        {
            get { return (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null; }
        }

        public string GetSummaryText(int screenWidth, int screenHeight)
        {
            return TcsAggregator.BuildSummaryPage(CurrentVessel, screenWidth);
        }

        public string GetLoopsText(int screenWidth, int screenHeight)
        {
            return TcsAggregator.BuildLoopsPage(CurrentVessel, screenWidth, ref loopsOffset);
        }

        public string GetReactorsText(int screenWidth, int screenHeight)
        {
            return TcsAggregator.BuildReactorsPage(CurrentVessel, screenWidth, ref reactorsOffset);
        }

        public void LoopsButtons(int buttonID)
        {
            ListButtons(buttonID, false, ref loopsOffset);
        }

        public void ReactorsButtons(int buttonID)
        {
            ListButtons(buttonID, true, ref reactorsOffset);
        }

        private void ListButtons(int buttonID, bool reactors, ref int scrollOffset)
        {
            if (buttonID == buttonDown)
            {
                TcsAggregator.TryScrollDown(CurrentVessel, reactors, ref scrollOffset);
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
