namespace MFDExtension.Elec
{
    // MAS companion module for the ELEC bay (R3) - DynamicBatteryStorage's
    // electrical ledger on three pages. Registered as a sibling MODULE on
    // MAS_JSI_BasicMFD (MAS_BasicMFD.cfg section 7); see MFDExtCasModule for
    // why this must be an InternalModule and for the method signatures.
    //
    // ONE module serves all three pages: each names its own textmethod and its
    // own buttonClickMethod (both free-form in cfg), which is what gives every
    // page its own scroll offset while they share one ledger mode.
    public class MFDExtElecModule : InternalModule
    {
        // Physical button ids, see MAS_JSI_BasicMFD.cfg's right-column comment.
        // Configurable from the cfg, defaulting to the real ids.
        [KSPField]
        public int buttonMode = 2;  // button_ENTER, the green left arrow
        [KSPField]
        public int buttonUp = 0;
        [KSPField]
        public int buttonDown = 1;
        [KSPField]
        public int buttonHome = 4;  // the white circle
        [KSPField]
        public int buttonExpand = 5;   // RIGHT, one row per part
        [KSPField]
        public int buttonCompact = 6;  // LEFT, symmetry groups folded into one row

        // Per-prop-instance state, not persisted: a fresh IVA starts in PLANT
        // mode, compact, at the top of each list.
        private bool totalMode;
        private bool compact = true;
        private int sourcesOffset;
        private int loadsOffset;

        private Vessel CurrentVessel
        {
            get { return (internalProp != null && internalProp.part != null) ? internalProp.part.vessel : null; }
        }

        public string GetSummaryText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildSummaryPage(CurrentVessel, totalMode, compact, screenWidth);
        }

        public string GetSourcesText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildListPage(CurrentVessel, ElecSide.Sources, totalMode, compact, screenWidth,
                                                ref sourcesOffset);
        }

        public string GetLoadsText(int screenWidth, int screenHeight)
        {
            return ElecAggregator.BuildListPage(CurrentVessel, ElecSide.Loads, totalMode, compact, screenWidth,
                                                ref loadsOffset);
        }

        // The summary scrolls nothing, but its TOP LOADS / TOP SOURCES digest
        // folds like the lists do, so both mode and density apply.
        public void SummaryButtons(int buttonID)
        {
            if (buttonID == buttonMode) ToggleMode();
            else if (buttonID == buttonExpand || buttonID == buttonCompact) SetCompact(buttonID == buttonCompact);
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
            else if (buttonID == buttonExpand || buttonID == buttonCompact)
            {
                SetCompact(buttonID == buttonCompact);
            }
            else if (buttonID == buttonDown)
            {
                ElecAggregator.TryScrollDown(CurrentVessel, side, totalMode, compact, ref scrollOffset);
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

        // Row i means a different entry once rows are folded, so the offsets
        // restart with the view.
        private void SetCompact(bool value)
        {
            if (compact == value) return;
            compact = value;
            sourcesOffset = 0;
            loadsOffset = 0;
        }
    }
}
