using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
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
    // (which states it shows, which tiers, text layout, color tags) on top
    // of them.
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
        // this text page. Inline color tags confirmed usable in a MAS_PAGE
        // TEXT node's content (real example: MOARdV/MFD/IFMS/MFD_Flight0.cfg
        // line 308, "[#ffff9b]") - that example is a static `text=` field,
        // not a textmethod-returned string, so this is the first time this
        // project relies on the tag inside dynamically generated text.
        // Flag as the least-verified part of this page if colors don't
        // render on first test.
        private const string WarningColorTag = "[#BF2626FF]";
        private const string CautionColorTag = "[#FFBF00FF]";
        private const string AdvisoryColorTag = "[#FF00FFFF]";
        private const string ResetColorTag = "[#FFFFFFFF]";

        private const int EntryIndent = 2;

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

        // MdVTextMesh splits rows on Utility.LineSeparator, which is
        // { Environment.NewLine } - "\r\n" on Windows (verified on the real
        // source, Utility.cs/MdVTextMesh.cs, 2026-08-23). A bare '\n' never
        // splits there: the whole page renders as ONE row with everything past
        // the header clipped off the right edge of the screen - exactly the
        // first in-game test's symptom. Never hardcode '\n' in page text.
        private static readonly string NL = Environment.NewLine;

        // This prop's REAL visible rows (40x20, see MAS_JSI_BasicMFD.cfg's
        // MASMonitor MODULE) - NOT the (40,32) MASPageText always passes to
        // a textmethod regardless of true screen size (verified against the
        // real MAS source, see CLAUDE.md). Rows past the 20th are silently
        // not shown - a known project-wide gotcha, load-bearing here because
        // the whole point of this file's scroll/collapse machinery is to
        // never let that silent cutoff hide a real alert.
        private const int VisibleRows = 20;
        private const int HeaderRows = 2; // "FAULT SUMMARY..." + dashes
        // Status line is unconditional (2026-08-30, user's call): the
        // "X-Y of N" summary is useful even when nothing is truncated (a
        // running total the per-tier header counts don't give you), and
        // it's where the key legend lives.
        private const int StatusLineRows = 1;
        // ...and it gets its own rule above it (2026-09-01, user's call,
        // explicitly worth one row of body budget): it reads as a footer
        // separated from the list rather than as one more list row, and
        // bookends the page against the identical rule under the header.
        private const int StatusSeparatorRows = 1;
        private const int BodyBudget = VisibleRows - HeaderRows - StatusSeparatorRows - StatusLineRows;

        // Tier now comes from MFDExtension.Shared (2026-08-24 refactor) -
        // the 4-level scale VVEFIS also uses. CAS simply never emits
        // Indication (an alerting page lists anomalies only); that's this
        // aggregator's own policy, not a smaller scale.
        private readonly struct AlertEntry
        {
            internal readonly Tier Tier;
            internal readonly string Label; // specific fault name when known ("ALTERNATOR", "STALL"...), "FAILURE" as the generic fallback - variable width, see AppendEntries
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

        // `scrollOffset` is an index into the flat WARNING-then-CAUTION-then-
        // ADVISORY entry sequence (matching the status line's "X-Y of N"),
        // owned by the caller (MFDExtCasModule, one per prop instance) and
        // threaded through by ref so this method can clamp it - e.g. after a
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

            if (scrollOffset < 0) scrollOffset = 0;
            if (scrollOffset > total - 1) scrollOffset = total - 1;

            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, screenWidth, warnBuffer.Count, cautionBuffer.Count, advisoryBuffer.Count);

            int entriesShown = AppendBody(sb, scrollOffset, screenWidth, out int bodyRowsUsed);

            // The status line always sits on the LAST visible row, not
            // wherever the body happened to end - a short list (nothing
            // truncated) was leaving it stuck right under the last content
            // line instead of anchored to the bottom of the screen (bug
            // found 2026-08-30). Pad with blank rows up to the body budget.
            for (int i = bodyRowsUsed; i < BodyBudget; ++i)
            {
                sb.Append(NL);
            }
            sb.Append(new string('-', screenWidth)).Append(NL);
            AppendStatusLine(sb, total, scrollOffset, entriesShown, screenWidth);

            return sb.ToString();
        }

        // The DOWN button's decision of whether to advance at all - see
        // MFDExtCasModule.ButtonProcessor. Deliberately separate from
        // BuildPage's own clamp: that one only guards against a stale offset
        // after the list shrinks, this one is a policy call (never push the
        // last entry alone to the top of an otherwise-empty screen - see
        // CLAUDE.md, "il tuo ultimo punto" discussion 2026-08-30). Because
        // ButtonProcessor calls this once per physical button press (a rare,
        // human-paced event), re-collecting entries here instead of caching
        // BuildPage's last result is a non-issue - BuildPage itself already
        // re-collects every ~20ms via the textmethod poll.
        internal static void TryScrollDown(Vessel vessel, ref int scrollOffset)
        {
            CollectAllEntries(vessel);
            int total = warnBuffer.Count + cautionBuffer.Count + advisoryBuffer.Count;
            if (total == 0)
            {
                scrollOffset = 0;
                return;
            }
            if (scrollOffset > total - 1) scrollOffset = total - 1;
            if (scrollOffset < 0) scrollOffset = 0;

            if (ReachesEnd(scrollOffset, warnBuffer.Count, cautionBuffer.Count, advisoryBuffer.Count))
            {
                return; // already showing every remaining entry individually - nothing more to reveal
            }
            scrollOffset++;
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

        // The heart of the scroll/collapse design (worked out with the user
        // 2026-08-30, see CLAUDE.md for the full back-and-forth). Walks the
        // three tiers in fixed WARNING/CAUTION/ADVISORY severity order,
        // starting at `scrollOffset`:
        //  - a tier entirely before scrollOffset is skipped outright - once
        //    scrolled past, it's just gone, like any ordinary list;
        //  - the first tier reached, and every later tier that still fits
        //    fully within the remaining budget, expands completely (header +
        //    every one of its entries);
        //  - the tier where the budget finally runs out shows as many
        //    entries as fit, then "+N MORE" for the rest;
        //  - every tier after THAT one - not yet reached at all - collapses
        //    to a single "TIER (N)" preview line instead of vanishing, so the
        //    operator always knows it exists without having to scroll there.
        // Returns how many real entries were actually printed as individual
        // lines (excludes header/collapse/+MORE lines), used by the status
        // line's "X-Y of N".
        private static int AppendBody(StringBuilder sb, int scrollOffset, int screenWidth, out int rowsUsed)
        {
            List<AlertEntry>[] tierLists = { warnBuffer, cautionBuffer, advisoryBuffer };
            string[] tierLabels = { "WARNING", "CAUTION", "ADVISORY" };
            string[] tierColors = { WarningColorTag, CautionColorTag, AdvisoryColorTag };

            int startTier = 0, localStart = 0, consumed = 0;
            for (; startTier < tierLists.Length; ++startTier)
            {
                int count = tierLists[startTier].Count;
                if (scrollOffset < consumed + count)
                {
                    localStart = scrollOffset - consumed;
                    break;
                }
                consumed += count;
            }

            rowsUsed = 0;
            int entriesShown = 0;
            for (int t = startTier; t < tierLists.Length; ++t)
            {
                List<AlertEntry> list = tierLists[t];
                int from = (t == startTier) ? localStart : 0;
                int remaining = Math.Max(0, list.Count - from);

                // An empty tier can only ever appear AFTER startTier (an
                // entry index can't land inside a zero-count tier, so
                // startTier itself is always non-empty) - handled on its
                // own here instead of through the fit/truncate machinery
                // below, because it can never be "partially shown" (nothing
                // to defer with "+N MORE") and its 2-row cost (header +
                // "(none)") must never be written unconditionally: doing so
                // without checking remaining room was a real bug (found
                // 2026-08-30) that could push the body past BodyBudget -
                // e.g. 16 CAUTION-only entries (exactly filling the 17-row
                // budget on their own) followed by an unconditional empty
                // ADVISORY "header + (none)" pushed the real body to 19
                // rows, past the 20 visible, silently endangering the
                // status line below it. An empty tier carries no
                // information the page header's W:/C:/A: counts don't
                // already give, so when there's no room left it is simply
                // omitted - never truncated, never the reason a real entry
                // goes missing.
                int laterNonEmpty = 0;
                for (int later = t + 1; later < tierLists.Length; ++later)
                {
                    if (tierLists[later].Count > 0) laterNonEmpty++;
                }

                if (list.Count == 0)
                {
                    // ...and `laterNonEmpty` has to be part of that check
                    // too: an empty tier's 2 rows are NOT covered by the
                    // previous tier's own fit test (which only ever reserves
                    // one row per later NON-empty tier), so without counting
                    // what still has to come after it, an empty tier could
                    // eat a row a later real tier was promised.
                    if (rowsUsed + 2 + laterNonEmpty <= BodyBudget)
                    {
                        sb.Append(tierColors[t]).Append(tierLabels[t]).Append(ResetColorTag).Append(NL);
                        sb.Append(' ', EntryIndent).Append("(none)").Append(NL);
                        rowsUsed += 2;
                    }
                    continue;
                }

                int fullyExpandedRows = 1 /* header */ + remaining;
                if (rowsUsed + fullyExpandedRows + laterNonEmpty <= BodyBudget)
                {
                    sb.Append(tierColors[t]).Append(tierLabels[t]).Append(ResetColorTag).Append(NL);
                    AppendEntries(sb, list, from, list.Count, screenWidth);
                    rowsUsed += fullyExpandedRows;
                    entriesShown += remaining;
                }
                else
                {
                    int reservedTail = 1 /* header */ + 1 /* +MORE */ + laterNonEmpty;
                    int availableForEntries = Math.Max(0, BodyBudget - rowsUsed - reservedTail);
                    int shown = Math.Min(availableForEntries, remaining);

                    if (shown == 0)
                    {
                        // No room for even one of this tier's entries - so it
                        // IS a collapsed tier, and must render as the same
                        // single "TIER (N)" line every not-yet-reached tier
                        // below uses. Writing header + "+N MORE" here (2
                        // rows) instead was a real bug, found in game
                        // 2026-08-30 from the user's screenshots: the
                        // PREVIOUS tier's fit test only ever reserves ONE row
                        // per later non-empty tier, so those 2 rows pushed
                        // the body to 18 and shoved the status line off the
                        // bottom of the screen entirely (W:9 C:6 A:5 at
                        // scrollOffset 1 - WARNING and CAUTION both expanded
                        // fully, leaving exactly 1 row for ADVISORY).
                        // Collapsing here costs exactly the 1 row that was
                        // reserved, and says the same thing more compactly.
                        // Count inside the color tag, not after it - a
                        // collapsed tier's whole line carries that tier's
                        // severity color (2026-09-01, user's call after
                        // seeing "ADVISORY" in magenta but "(6)" in white).
                        sb.Append(tierColors[t]).Append(tierLabels[t])
                          .Append(" (").Append(remaining).Append(')').Append(ResetColorTag).Append(NL);
                        rowsUsed += 1;
                    }
                    else
                    {
                        sb.Append(tierColors[t]).Append(tierLabels[t]).Append(ResetColorTag).Append(NL);
                        AppendEntries(sb, list, from, from + shown, screenWidth);
                        entriesShown += shown;
                        rowsUsed += 1 + shown;

                        int leftover = remaining - shown;
                        if (leftover > 0)
                        {
                            sb.Append(' ', EntryIndent).Append('+').Append(leftover).Append(" MORE").Append(NL);
                            rowsUsed += 1;
                        }
                    }

                    for (int later = t + 1; later < tierLists.Length; ++later)
                    {
                        if (tierLists[later].Count > 0)
                        {
                            sb.Append(tierColors[later]).Append(tierLabels[later])
                              .Append(" (").Append(tierLists[later].Count).Append(')').Append(ResetColorTag).Append(NL);
                            rowsUsed += 1;
                        }
                    }
                    break;
                }
            }

            return entriesShown;
        }

        // Same fit/no-fit test AppendBody performs, without actually
        // rendering - used only to decide whether the DOWN button should be
        // allowed to advance (TryScrollDown). "Reaches the end" means every
        // remaining entry, in every tier from `scrollOffset` onward, would
        // print as an individual line - no trailing "+N MORE", no collapsed
        // tier left over.
        private static bool ReachesEnd(int scrollOffset, int warnCount, int cautionCount, int advisoryCount)
        {
            int total = warnCount + cautionCount + advisoryCount;
            if (total == 0) return true;
            if (scrollOffset >= total - 1) return true;

            int[] counts = { warnCount, cautionCount, advisoryCount };
            int consumed = 0;
            int rowsNeeded = 0;
            bool reached = false;
            for (int i = 0; i < counts.Length; ++i)
            {
                int tierCount = counts[i];
                int tierStart = consumed;
                consumed += tierCount;
                if (tierCount == 0) continue;

                if (!reached)
                {
                    if (scrollOffset >= consumed) continue; // entirely before scrollOffset
                    reached = true;
                    int localStart = Math.Max(0, scrollOffset - tierStart);
                    rowsNeeded += 1 + (tierCount - localStart);
                }
                else
                {
                    rowsNeeded += 1 + tierCount;
                }
            }

            return rowsNeeded <= BodyBudget;
        }

        private static void AppendEntries(StringBuilder sb, List<AlertEntry> list, int from, int to, int screenWidth)
        {
            if (list.Count == 0)
            {
                sb.Append(' ', EntryIndent).Append("(none)").Append(NL);
                return;
            }

            int titleBudget = screenWidth - EntryIndent - LabelColumnWidth - 1;
            for (int i = from; i < to; ++i)
            {
                AlertEntry e = list[i];
                // Fixed-width label column (see LabelColumnWidth) - every
                // row's title starts at the same position regardless of
                // that row's own label length. TruncateLabel abbreviates or
                // (last resort) ellipsis-shortens anything over budget -
                // applied here, not at collection time, so it covers every
                // source (DangIt/FAR/RealBattery) from one place.
                sb.Append(' ', EntryIndent).Append(TruncateLabel(e.Label).PadRight(LabelColumnWidth)).Append(' ')
                  .Append(ScrollingTitle(e.PartTitle, titleBudget)).Append(NL);
            }
        }

        // A part's title that overflows its column budget scrolls (marquee)
        // instead of being cut short with an ellipsis, so the full name is
        // eventually shown rather than permanently hidden - the user's own
        // request, 2026-08-30 ("il part.title... può essere scorrevole?").
        // Speed and gap are tuned for readability, not for CAS's own poll
        // rate: textmethod is called every FixedUpdate (MASPageText.cs's
        // TextMethodUpdate coroutine), far faster than any human reads text,
        // so we drive the animation off real elapsed seconds
        // (Time.realtimeSinceStartup, immune to time warp/pause) rather than
        // off the poll itself - the two are otherwise unrelated.
        private const float ScrollCharsPerSecond = 4f;
        private const string ScrollGap = "   "; // seam between the end and the repeat, marks the wrap instead of jumping straight back

        private static string ScrollingTitle(string text, int width)
        {
            if (width <= 0) return string.Empty;
            if (text.Length <= width) return text;

            string loop = text + ScrollGap;
            int period = loop.Length;
            int offset = (int)(Time.realtimeSinceStartup * ScrollCharsPerSecond) % period;

            StringBuilder window = new StringBuilder(width);
            for (int i = 0; i < width; ++i)
            {
                window.Append(loop[(offset + i) % period]);
            }
            return window.ToString();
        }

        // "X-Y of N" - a running total the per-tier header counts don't give
        // (those are scorporati per severità, not summed), and confirmation
        // that nothing further remains once Y == N.
        // Two halves, pushed to opposite edges (2026-09-01): the position
        // readout stays left, the key legend is flushed right, so the two
        // read as separate things instead of one run-on line.
        //
        // Glyphs went through three rounds against what the font actually
        // renders: "^v" (caret far smaller than "v" in InconsolataGo), then
        // "Λv" (which merely inverted the mismatch - lowercase v now small
        // beside a full-height Λ), now "ΛV" with both uppercase. Home was
        // "<", then "O" - but a monospace capital O reads as an oval/zero,
        // so it's now U+25CB WHITE CIRCLE ("○"), an actual circle matching
        // the button's own face. FLAG IF IT RENDERS AS A BOX: unlike Λ
        // (already confirmed rendering in game), this glyph's presence in
        // the monitor's font is unverified.
        //
        // buttonHome = 4 itself was always correct (user-confirmed, "restiamo
        // fedeli al layout di MAS") - every change here has been to the
        // legend text, never the binding.
        private const string KeyLegend = "ΛV: scroll  ○: home";

        private static void AppendStatusLine(StringBuilder sb, int total, int scrollOffset, int entriesShown, int screenWidth)
        {
            int first = scrollOffset + 1;
            int last = scrollOffset + entriesShown;
            string position = first + "-" + last + " of " + total;

            int gap = screenWidth - position.Length - KeyLegend.Length;
            string line = gap > 0 ? position + new string(' ', gap) + KeyLegend : position + " " + KeyLegend;
            if (line.Length > screenWidth) line = line.Substring(0, screenWidth);
            sb.Append(line);
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
