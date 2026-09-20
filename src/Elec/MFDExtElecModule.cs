namespace MFDExtension.Elec
{
    // MAS companion module for the ELEC bay (R3) - DynamicBatteryStorage's
    // electrical ledger on three pages. Registered as a sibling MODULE on
    // MAS_JSI_BasicMFD (Config/Additive/MAS_BasicMFD.cfg section 7), same
    // "manual companion MODULE" pattern as MFDExtCasModule - read that file
    // for why this must be an InternalModule and for the textmethod /
    // RPM_MODULE signatures (both verified on MAS's real source).
    //
    // ONE module serves all three pages: each page's TEXT node names its own
    // textmethod and each page's RPM_MODULE names its own buttonClickMethod
    // (the method name is free-form cfg, exactly like NavInstruments'
    // ButtonProcessor), which is what lets every page keep its own scroll
    // offset while sharing one ledger mode.
    public class MFDExtElecModule : InternalModule
    {
        // Physical button ids, see MAS_JSI_BasicMFD.cfg's right-column
        // comment (UP 0, DOWN 1, ENTER 2, ESC 3, HOME 4). Configurable from
        // the cfg like CAS's, defaulting to the real ids.
        [KSPField]
        public int buttonMode = 2;  // button_ENTER, the green left arrow (user's call 2026-09-17; first bound to ESC/3)
        [KSPField]
        public int buttonUp = 0;
        [KSPField]
        public int buttonDown = 1;
        [KSPField]
        public int buttonHome = 4;  // the white circle

        // Per-prop-instance state (a second ELEC monitor on the same vessel
        // scrolls and switches mode independently). Not persisted: a fresh
        // IVA always starts in PLANT mode at the top of each list.
        private bool totalMode;
        private int sourcesOffset;
        private int loadsOffset;

        private Vessel CurrentVessel
        {
            get { return (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null; }
        }

        public string GetSummaryText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildSummaryPage(CurrentVessel, totalMode, screenWidth);
        }

        public string GetSourcesText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildListPage(CurrentVessel, ElecSide.Sources, totalMode, screenWidth, ref sourcesOffset);
        }

        public string GetLoadsText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildListPage(CurrentVessel, ElecSide.Loads, totalMode, screenWidth, ref loadsOffset);
        }

        // The summary is static: only the mode key does anything.
        public void SummaryButtons(int buttonID)
        {
            if (buttonID == buttonMode) ToggleMode();
        }

        public void SourcesButtons(int buttonID)
        {
            ListButtons(buttonID, ElecSide.Sources, ref sourcesOffset);
        }

        public void LoadsButtons(int buttonID)
        {
            ListButtons(buttonID, ElecSide.Loads, ref loadsOffset);
        }

        private void ListButtons(int buttonID, ElecSide side, ref int scrollOffset)
        {
            if (buttonID == buttonMode)
            {
                ToggleMode();
            }
            else if (buttonID == buttonDown)
            {
                ElecAggregator.TryScrollDown(CurrentVessel, side, totalMode, ref scrollOffset);
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

        // The storage group moves (last in PLANT, by magnitude in TOTAL), so
        // an offset kept across the switch would point at a different entry.
        private void ToggleMode()
        {
            totalMode = !totalMode;
            sourcesOffset = 0;
            loadsOffset = 0;
        }
    }
}
