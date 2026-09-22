namespace MFDExtension.Tcs
{
    // MAS companion module for the TCS bay (R4, Thermal Control System) -
    // SystemHeat's loops, members and reactors on three pages. Registered as a
    // sibling MODULE on MAS_JSI_BasicMFD (MAS_BasicMFD.cfg section 8); see
    // MFDExtCasModule for why this must be an InternalModule.
    //
    // ONE module serves all three pages, each naming its own textmethod and
    // buttonClickMethod, which is what gives every page its own scroll offset.
    // The summary page binds no key and so carries no RPM_MODULE at all.
    public class MFDExtTcsModule : InternalModule
    {
        // Physical button ids, see MAS_JSI_BasicMFD.cfg's right-column comment.
        // Configurable from the cfg, defaulting to the real ids.
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

        // Per-prop-instance state, not persisted. Lists start compact: a
        // vessel's symmetry groups would otherwise spend the whole page on
        // identical rows before the player can press anything.
        private bool compact = true;
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
            return TcsAggregator.BuildLoopsPage(CurrentVessel, compact, screenWidth, ref loopsOffset);
        }

        public string GetReactorsText(int screenWidth, int screenHeight)
        {
            return TcsAggregator.BuildReactorsPage(CurrentVessel, screenWidth, ref reactorsOffset);
        }

        public void LoopsButtons(int buttonID)
        {
            // Only LOOPS folds rows: a reactor block carries per-unit core
            // temperature, integrity and fuel life, which do not sum.
            if (buttonID == buttonExpand || buttonID == buttonCompact)
            {
                SetCompact(buttonID == buttonCompact);
                return;
            }
            ListButtons(buttonID, false, ref loopsOffset);
        }

        public void ReactorsButtons(int buttonID)
        {
            ListButtons(buttonID, true, ref reactorsOffset);
        }

        // Row i means a different member in the two views, so the offset can't
        // carry over - same rule ELEC applies to its ledger mode.
        private void SetCompact(bool value)
        {
            if (compact == value) return;
            compact = value;
            loopsOffset = 0;
        }

        private void ListButtons(int buttonID, bool reactors, ref int scrollOffset)
        {
            if (buttonID == buttonDown)
            {
                TcsAggregator.TryScrollDown(CurrentVessel, reactors, compact, ref scrollOffset);
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
