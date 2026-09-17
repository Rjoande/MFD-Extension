using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MFDExtension.Pages;
using MFDExtension.Shared;

namespace MFDExtension.Cas
{
    // Builds the CAS (Crew Alerting System) fault-summary page text - design
    // discussed and confirmed with the user 2026-08-19, see CLAUDE.md and
    // HOSTING.md for the full history.
    //
    // Deliberately narrower than VVEFIS's own VVEFISSeverity.GetStatus (see
    // Extras/VVEFIS/src/VVEFISSeverity.cs): reads DangIt failures, FAR stall
    // warnings, and (2026-08-24) three RealBattery malfunction states - never
    // fuel or generic engine condition. Confirmed with the user: fuel is
    // already covered by the native RES page and various RPM/MAS/ASET props;
    // an "expected" condition (an idle engine, a partially-full tank, a
    // battery just running low or sitting disabled) isn't CAS material -
    // only genuine anomalies belong on an alerting page. RealBattery's own
    // charge level (SC_SOC) and BatteryDisabled stay excluded for the exact
    // same reason FUEL is - bay B already covers SC_SOC continuously, and
    // "disabled" alone isn't a malfunction. Since the 2026-08-24 "shared
    // basket" refactor the readers themselves (DangItBridge/FARBridge/
    // RealBatteryBridge, the Tier scale, mod presence) live in src/Shared/
    // and are shared with VVEFIS - this file is the CAS-specific policy
    // (which states it shows, which tiers, labels, colors) on top of them.
    //
    // Since 2026-09-16 (log 85) the page LAYOUT - scroll offset, per-tier
    // expand/truncate/collapse, "+N MORE", bottom-anchored status line with
    // the key legend, marquee titles - lives in src/Pages/ScrollingListPage
    // and is shared with the ELEC/TCS bays. It was designed and debugged in
    // game right here (logs 69-79) and moved out verbatim, regression-tested
    // against a copy of the old code. This file feeds it three ListGroups
    // (WARNING/CAUTION/ADVISORY) and renders each entry row.
    //
    // A part can contribute more than one entry (e.g. a DangIt failure AND
    // a FAR stall at once) - these are independent problems, listed
    // separately, not merged into "worst wins" the way VVEFISSeverity does
    // for a single fill color.
    internal static class CasAggregator
    {
        // Colors match VVEFISSeverity's own WARNING/CAUTION/ADVISORY palette
        // exactly (converted from its Unity Color values), so a part's
        // severity reads the same whether you're looking at the 3D view or
        // this text page. Inline color tags inside a textmethod-returned
        // string confirmed rendering in game (log 78).
        private const string WarningColorTag = "[#BF2626FF]";
        private const string CautionColorTag = "[#FFBF00FF]";
        private const string AdvisoryColorTag = "[#FF00FFFF]";

        // Fixed so every row's part title starts in the same column instead
        // of drifting with each row's own label length (bug found
        // 2026-08-30: a ragged left edge made the scrolling titles look
        // broken, each one starting/wrapping at a different point). Width
        // walked 18 -> 12 -> 14 -> 7 as the label set was measured rather
        // than guessed (full table of all 25 labels with lengths, and the
        // stats behind this, in CLAUDE.md 2026-09-01). 7 is a deliberate
        // choice by the user with the tradeoff spelled out: it sits just
        // above the label-length median (7) and mode (6), so over half the
        // set still fits natively, and it hands the part title 30 of the 37
        // usable characters (screenWidth 40 - indent 2 - 1 separator) -
        // enough that a typical stock title stops scrolling entirely
        // ("Serbatoio esterno R-4 'Gnocco'" is exactly 30).
        //
        // The cost, accepted knowingly: 12 of the 25 labels now depend on
        // LabelAbbreviations below, so that table is the primary mechanism
        // rather than a handful of exceptions. On a LOCALIZED install none
        // of its English keys match and every long label falls through to
        // TruncateLabel's ellipsis, which bites much harder at 7 than it
        // did at 14 - same for any future DangIt module or third-party
        // FailureModule this table doesn't know. Abbreviating rather than
        // scrolling remains right either way: two marquees animating on one
        // row would be unreadable.
        private const int LabelColumnWidth = 7;

        // Every label CAS can emit that exceeds LabelColumnWidth, hand
        // abbreviated (grep of every ScreenName override in DangIt-master
        // resolved against en-us.cfg, plus the non-DangIt labels - full
        // table in CLAUDE.md 2026-09-01). Aviation/aerospace shorthand, not
        // truncation: still unambiguous to a pilot at a glance. Reviewed
        // and approved one by one by the user; RWA in particular is the
        // real spacecraft-engineering initialism (Reaction Wheel Assembly),
        // chosen over a "RCT WHL" contraction on their call. ALTR rather
        // than ALT because ALT means altitude on a flight deck; CTL SRF
        // rather than the exactly-fitting SURFACE because that reads as the
        // planetary surface in this context.
        private static readonly Dictionary<string, string> LabelAbbreviations = new Dictionary<string, string>
        {
            { "OVERHEAT", "OVHT" },
            { "DECOUPLER", "DCPLR" },
            { "GENERATOR", "GEN" },
            { "ALTERNATOR", "ALTR" },
            { "LIGHT BULB", "LAMP" },
            { "COOLANT LINE", "COOLANT" },
            { "RCS THRUSTER", "RCS" },
            { "REACTION WHEEL", "RWA" },
            { "TRACKING SERVO", "TRK SRV" },
            { "CONTROL SURFACE", "CTL SRF" },
            { "PRESSURE VESSEL", "PRS VSL" },
            { "DEPLOYABLE ANTENNA", "ANTENNA" },
        };

        private static string TruncateLabel(string label)
        {
            if (LabelAbbreviations.TryGetValue(label, out string shortLabel)) label = shortLabel;
            if (label.Length <= LabelColumnWidth) return label;
            return label.Substring(0, LabelColumnWidth - 1) + "…"; // last-resort fallback, not expected to fire
        }

        private static readonly string NL = ScrollingListPage.NL;

        // Tier now comes from MFDExtension.Shared (2026-08-24 refactor) -
        // the 4-level scale VVEFIS also uses. CAS simply never emits
        // Indication (an alerting page lists anomalies only); that's this
        // aggregator's own policy, not a smaller scale.
        private readonly struct AlertEntry
        {
            internal readonly Tier Tier;
            internal readonly string Label; // specific fault name when known ("ALTERNATOR", "STALL"...), "FAILURE" as the generic fallback
            internal readonly string PartTitle;

            internal AlertEntry(Tier tier, string label, string partTitle)
            {
                Tier = tier;
                Label = label;
                PartTitle = partTitle;
            }
        }

        // Reused across frames - BuildPage runs continuously while the page is
        // up (MASPageText's TextMethodUpdate coroutine), keep per-call garbage
        // down. Single-threaded like everything else on the prop (a Unity
        // coroutine never runs two of its own steps concurrently, so sharing
        // these across possibly more than one CAS-bearing monitor is safe -
        // same assumption dangItBuffer already relied on).
        private static readonly List<DangItBridge.FailureInfo> dangItBuffer = new List<DangItBridge.FailureInfo>();
        private static readonly List<AlertEntry> entryBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> warnBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> cautionBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> advisoryBuffer = new List<AlertEntry>();

        // The three groups handed to the page engine, in fixed severity
        // order - built once, only their Count changes per frame. The render
        // callbacks read the buffers above directly.
        private static readonly ListGroup[] groups =
        {
            new ListGroup { Label = "WARNING",  ColorTag = WarningColorTag,  RenderEntry = (sb, i, w) => RenderEntry(sb, warnBuffer[i], w) },
            new ListGroup { Label = "CAUTION",  ColorTag = CautionColorTag,  RenderEntry = (sb, i, w) => RenderEntry(sb, cautionBuffer[i], w) },
            new ListGroup { Label = "ADVISORY", ColorTag = AdvisoryColorTag, RenderEntry = (sb, i, w) => RenderEntry(sb, advisoryBuffer[i], w) },
        };

        // `scrollOffset` is an index into the flat WARNING-then-CAUTION-then-
        // ADVISORY entry sequence (matching the status line's "X-Y of N"),
        // owned by the caller (MFDExtCasModule, one per prop instance) and
        // threaded through by ref so the engine can clamp it - e.g. after a
        // failure gets repaired and the list shrinks mid-scroll.
        internal static string BuildPage(Vessel vessel, int screenWidth, int screenHeight, ref int scrollOffset)
        {
            // "DangIt" is the classic DLL's CLR name; "DangItContinued" is
            // linuxgurugamer's fork, confirmed as the name on the user's
            // real install (2026-08-24) - listing both keeps this working
            // on either. This workspace's own copy of DangItContinued.dll
            // declares no KSPAssembly attribute at all (checked on its raw
            // metadata), so ModPresence's CLR-name comparison is the only
            // reliable check either way - see ModPresence.cs.
            bool dangItLoaded = ModPresence.IsLoaded("DangIt", "DangItContinued");
            bool farLoaded = ModPresence.IsLoaded("FerramAerospaceResearch");
            bool realBatteryLoaded = ModPresence.IsLoaded("RealBattery");

            if (!dangItLoaded && !farLoaded && !realBatteryLoaded)
            {
                return NoSourcesPage(screenWidth);
            }

            CollectAllEntries(vessel);
            int total = warnBuffer.Count + cautionBuffer.Count + advisoryBuffer.Count;

            if (total == 0)
            {
                scrollOffset = 0;
                return NominalPage(screenWidth);
            }

            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, screenWidth, warnBuffer.Count, cautionBuffer.Count, advisoryBuffer.Count);
            // Empty tiers still print as "TIER" + "(none)" when there's room
            // (CAS policy since the first version; the ELEC/TCS bays omit
            // empty groups instead).
            ScrollingListPage.AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups: true);
            return sb.ToString();
        }

        // The DOWN button - see MFDExtCasModule.ButtonProcessor. Because
        // ButtonProcessor calls this once per physical button press (a rare,
        // human-paced event), re-collecting entries here instead of caching
        // BuildPage's last result is a non-issue - BuildPage itself already
        // re-collects every ~20ms via the textmethod poll.
        internal static void TryScrollDown(Vessel vessel, ref int scrollOffset)
        {
            CollectAllEntries(vessel);
            ScrollingListPage.TryScrollDown(groups, ref scrollOffset);
        }

        private static void CollectAllEntries(Vessel vessel)
        {
            entryBuffer.Clear();
            if (vessel != null)
            {
                foreach (Part part in vessel.Parts)
                {
                    CollectDangIt(part, entryBuffer);
                    CollectFar(part, entryBuffer);
                    CollectRealBattery(part, entryBuffer);
                }
            }

            warnBuffer.Clear();
            cautionBuffer.Clear();
            advisoryBuffer.Clear();
            foreach (AlertEntry e in entryBuffer)
            {
                if (e.Tier == Tier.Warning) warnBuffer.Add(e);
                else if (e.Tier == Tier.Caution) cautionBuffer.Add(e);
                else advisoryBuffer.Add(e);
            }
            groups[0].Count = warnBuffer.Count;
            groups[1].Count = cautionBuffer.Count;
            groups[2].Count = advisoryBuffer.Count;
        }

        private static void CollectDangIt(Part part, List<AlertEntry> entries)
        {
            dangItBuffer.Clear();
            DangItBridge.CollectFailures(part, dangItBuffer);

            foreach (DangItBridge.FailureInfo failure in dangItBuffer)
            {
                // MapPriorityToTier (src/Shared/DangItBridge.cs) sends any
                // Priority string that isn't clearly HIGH/LOW to Caution -
                // an alerting page must never silently drop a real failure
                // over a label it doesn't recognize (DangIt's own C# default
                // is the raw tag "#LOC_DangIt_68" = MEDIUM in en-us, and a
                // localized install carries a translated string entirely).
                Tier tier = DangItBridge.MapPriorityToTier(failure.Priority);

                // Two failures on one part would otherwise be two identical
                // lines - DangIt's ScreenName ("Alternator", "Gimbal"...) now
                // leads the line instead of the generic "FAILURE", so they
                // read apart without parentheses. Uppercased to match the
                // WARNING/CAUTION/ADVISORY header caps convention. Falls back
                // to "FAILURE" if ScreenName came back null (reflection
                // failed, or a future DangIt drops the property).
                string label = string.IsNullOrEmpty(failure.Name) ? "FAILURE" : failure.Name.ToUpperInvariant();
                entries.Add(new AlertEntry(tier, label, PartTitle(part)));
            }
        }

        private static void CollectFar(Part part, List<AlertEntry> entries)
        {
            if (FARBridge.TryGetStall(part, out float stall) && stall >= FARBridge.StallWarningThreshold)
            {
                entries.Add(new AlertEntry(Tier.Warning, "STALL", PartTitle(part)));
            }
        }

        // Same precedence as VVEFISSeverity.GetRealBatteryStatus (NOT tier
        // order - Runaway beats Overheat beats EOL, checked in that fixed
        // order, first match wins) - but narrowed to the three states that
        // are genuine malfunctions. CAS never checks BatteryDisabled or
        // SC_SOC at all (design decision 2026-08-24, same reasoning as the
        // FUEL exclusion - see this file's header), which has one
        // consequence worth being explicit about: an EOL battery that later
        // gets disabled (by the player, or by RealBattery's own runaway/
        // overheat auto-shutoff) does NOT disappear from CAS. BatteryLife
        // and BatteryDisabled are independent fields in RealBattery's own
        // source (verified: reaching EOL_THRESHOLD only fires a one-time
        // toast, it never touches BatteryDisabled) - EOL is a standing
        // capacity fact, not a live "is it running" flag, and the EOL check
        // already runs before any Disabled check would in VVEFIS too, so
        // both channels agree here.
        private static void CollectRealBattery(Part part, List<AlertEntry> entries)
        {
            RealBatteryBridge.BatteryInfo battery = RealBatteryBridge.GetInfo(part);
            if (!battery.Present) return;

            if (battery.IsRunaway)
            {
                entries.Add(new AlertEntry(Tier.Warning, "RUNAWAY", PartTitle(part)));
                return;
            }
            if (battery.Overheating)
            {
                entries.Add(new AlertEntry(Tier.Caution, "OVERHEAT", PartTitle(part)));
                return;
            }
            if (battery.BatteryLife < RealBatteryBridge.EolThreshold)
            {
                entries.Add(new AlertEntry(Tier.Advisory, "EOL", PartTitle(part)));
            }
        }

        private static string PartTitle(Part part)
        {
            return (part.partInfo != null && !string.IsNullOrEmpty(part.partInfo.title)) ? part.partInfo.title : part.name;
        }

        private static void AppendHeader(StringBuilder sb, int screenWidth, int warnCount, int cautionCount, int advisoryCount)
        {
            string counts = "W:" + warnCount + " C:" + cautionCount + " A:" + advisoryCount;
            int padWidth = screenWidth - counts.Length;
            sb.Append(padWidth > 0 ? "FAULT SUMMARY".PadRight(padWidth) : "FAULT SUMMARY").Append(counts).Append(NL);
            sb.Append(new string('-', screenWidth)).Append(NL);
        }

        // One entry = one row: fixed-width label column (see
        // LabelColumnWidth), then the part title scrolling in whatever is
        // left. TruncateLabel abbreviates or (last resort) ellipsis-shortens
        // anything over budget - applied here, not at collection time, so it
        // covers every source (DangIt/FAR/RealBattery) from one place.
        private static void RenderEntry(StringBuilder sb, AlertEntry e, int screenWidth)
        {
            int titleBudget = screenWidth - ScrollingListPage.EntryIndent - LabelColumnWidth - 1;
            sb.Append(' ', ScrollingListPage.EntryIndent)
              .Append(TruncateLabel(e.Label).PadRight(LabelColumnWidth)).Append(' ')
              .Append(ScrollingListPage.Marquee(e.PartTitle, titleBudget, Time.realtimeSinceStartup))
              .Append(NL);
        }

        private static string NominalPage(int screenWidth)
        {
            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, screenWidth, 0, 0, 0);
            sb.Append(NL);
            sb.Append("ALL SYSTEMS NOMINAL");
            return sb.ToString();
        }

        private static string NoSourcesPage(int screenWidth)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("FAULT SUMMARY").Append(NL);
            sb.Append(new string('-', screenWidth)).Append(NL);
            sb.Append(NL);
            sb.Append("NO FAULT SOURCES DETECTED").Append(NL);
            sb.Append(NL);
            sb.Append("Install DangIt, FAR, or").Append(NL);
            sb.Append("RealBattery for active fault").Append(NL);
            sb.Append("monitoring.");
            return sb.ToString();
        }
    }
}
