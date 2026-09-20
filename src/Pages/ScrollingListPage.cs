using System;
using System.Collections.Generic;
using System.Text;

namespace MFDExtension.Pages
{
    // One group of a scrollable list page: a colored header line, N entries
    // of a fixed height each, rendered on demand by the owner. CAS uses three
    // of these (WARNING/CAUTION/ADVISORY), the ELEC bay one per DBS category,
    // the TCS bay one per heat loop (1-row members) or one per reactor page
    // (4-row entries) - the engine below doesn't care what an entry is, only
    // how many rows it takes.
    internal sealed class ListGroup
    {
        public string Label;                 // header text, uppercase by convention ("WARNING", "SOLAR PANELS", "LOOP 0")
        public string ColorTag;              // "[#RRGGBBAA]" applied to the header and to the collapsed "LABEL (N)" line
        // Optional text flushed to the right edge of the header line AND of
        // the collapsed "LABEL (N)" line (ELEC: a category subtotal). Added
        // 2026-09-17 (log 88); null = byte-identical output to before the
        // field existed, which is what keeps CAS at parity (harness re-run).
        public string HeaderRight;
        public int Count;                    // number of entries currently in the group
        public int RowsPerEntry = 1;         // every entry of a group has the same height
        // Appends entry `index` to `sb` as EXACTLY RowsPerEntry rows, each
        // terminated by ScrollingListPage.NL. Arguments: (sb, index, screenWidth).
        public Action<StringBuilder, int, int> RenderEntry;
    }

    // The scroll/collapse engine extracted verbatim (2026-09-16, CLAUDE.md log
    // 85) from CasAggregator, where it was designed and debugged in game with
    // the user between 2026-08-30 and 2026-09-02 (logs 69-79). Generalized in
    // exactly two ways: entries may span more than one row (RowsPerEntry), and
    // showing empty groups is a caller policy (CAS keeps its "(none)" rows,
    // the ELEC/TCS bays omit empty categories). Everything else - the fit
    // test, the "+N MORE" truncation, the collapsed "LABEL (N)" preview line
    // for groups not yet reached, the one-row reservation per later non-empty
    // group, the refusal to scroll past "everything remaining is visible" -
    // is the CAS behavior, kept identical and regression-tested against a
    // verbatim copy of the old code (scratchpad harness, log 85).
    //
    // Deliberately UnityEngine-free so it compiles in a plain console
    // harness: the marquee takes the clock as an argument instead of reading
    // Time.realtimeSinceStartup itself.
    //
    // Screen model (this prop's REAL 40x20, not the (40,32) MASPageText always
    // passes to a textmethod): rows past the 20th are silently dropped by
    // MAS, which is the whole reason this engine budgets rows at all. The
    // caller writes exactly HeaderRows rows first, then calls
    // AppendBodyAndStatus, which writes body + blank padding + rule + status
    // line = the remaining 18 rows, always.
    internal static class ScrollingListPage
    {
        public const int VisibleRows = 20;
        public const int HeaderRows = 2;          // title line + dashes, written by the caller
        public const int StatusSeparatorRows = 1; // "-----" above the status line (user's call 2026-09-01, worth one body row)
        public const int StatusLineRows = 1;      // unconditional (user's call 2026-08-30): "X-Y of N" + key legend
        public const int BodyBudget = VisibleRows - HeaderRows - StatusSeparatorRows - StatusLineRows; // 16
        public const int EntryIndent = 2;         // entries, "(none)" and "+N MORE" all indent by this

        public const string ResetColorTag = "[#FFFFFFFF]";

        // Key legend, right-flushed on the status line. Glyph history: "^v"
        // (caret too small in InconsolataGo), "Λv", "ΛV" (log 71/72), now
        // "▲▼" U+25B2/U+25BC (user's request, log 84) - UNVERIFIED in the
        // monitor's font at the time of writing; if they render as boxes,
        // fall back to "ΛV". "○" U+25CB for HOME is confirmed rendering in
        // game (log 78). buttonHome = 4 itself never changed.
        public const string KeyLegend = "▲▼: scroll  ○: home";

        // MdVTextMesh splits rows on Environment.NewLine ONLY ("\r\n" on
        // Windows) - a bare '\n' renders the whole page as one clipped row
        // (confirmed in game 2026-08-23). Never hardcode '\n' in page text.
        public static readonly string NL = Environment.NewLine;

        public static int TotalEntries(IList<ListGroup> groups)
        {
            int total = 0;
            for (int i = 0; i < groups.Count; ++i) total += groups[i].Count;
            return total;
        }

        // Guards only against a stale offset after the list shrinks (an entry
        // repaired mid-scroll); the policy of how far DOWN may go lives in
        // TryScrollDown.
        public static void ClampOffset(IList<ListGroup> groups, ref int scrollOffset)
        {
            int total = TotalEntries(groups);
            if (total == 0) { scrollOffset = 0; return; }
            if (scrollOffset < 0) scrollOffset = 0;
            if (scrollOffset > total - 1) scrollOffset = total - 1;
        }

        // Writes the body from `scrollOffset` (an index into the flat entry
        // sequence across all groups, matching the status line's "X-Y of N"),
        // pads with blank rows up to BodyBudget so the status line always sits
        // on the LAST visible row (bug found 2026-08-30 when it followed the
        // last content row instead), then the rule and the status line.
        public static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                               int screenWidth, bool showEmptyGroups)
        {
            AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups, KeyLegend);
        }

        // Same, with a page-specific key legend (a bay that binds one more
        // physical key than CAS's scroll/home pair says so on its own status
        // line - ELEC's "x: mode", log 88).
        public static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                               int screenWidth, bool showEmptyGroups, string keyLegend)
        {
            ClampOffset(groups, ref scrollOffset);
            int total = TotalEntries(groups);

            int entriesShown = AppendBody(sb, groups, scrollOffset, screenWidth, showEmptyGroups, out int rowsUsed);

            for (int i = rowsUsed; i < BodyBudget; ++i)
            {
                sb.Append(NL);
            }
            sb.Append('-', screenWidth).Append(NL);
            AppendStatusLine(sb, total, scrollOffset, entriesShown, screenWidth, keyLegend);
        }

        // The DOWN button's decision of whether to advance at all. Never pushes
        // the last entry alone to the top of an otherwise-empty screen:
        // incrementing by one from zero and refusing once every remaining
        // entry is already visible converges on exactly the smallest offset
        // that shows everything left (property worked out 2026-08-30, log 69).
        public static void TryScrollDown(IList<ListGroup> groups, ref int scrollOffset)
        {
            ClampOffset(groups, ref scrollOffset);
            if (TotalEntries(groups) == 0) return;
            if (ReachesEnd(groups, scrollOffset)) return;
            scrollOffset++;
        }

        // Walks the groups in order from the one containing `scrollOffset`:
        //  - a group entirely before scrollOffset is skipped outright;
        //  - the first group reached, and every later group that still fits
        //    fully within the remaining budget, expands completely (header +
        //    every remaining entry);
        //  - the group where the budget runs out shows as many entries as fit,
        //    then "+N MORE" - or, if not even one fits, renders as the single
        //    collapsed "LABEL (N)" line (log 79: writing header + "+N MORE"
        //    there cost 2 rows where only 1 had been reserved and pushed the
        //    status line off screen);
        //  - every later non-empty group collapses to its "LABEL (N)" line,
        //    whole line in the group's color (log 78).
        // Returns how many entries were printed individually (for "X-Y of N").
        private static int AppendBody(StringBuilder sb, IList<ListGroup> groups, int scrollOffset, int screenWidth,
                                      bool showEmptyGroups, out int rowsUsed)
        {
            int startGroup = 0, localStart = 0, consumed = 0;
            for (; startGroup < groups.Count; ++startGroup)
            {
                int count = groups[startGroup].Count;
                if (scrollOffset < consumed + count)
                {
                    localStart = scrollOffset - consumed;
                    break;
                }
                consumed += count;
            }

            rowsUsed = 0;
            int entriesShown = 0;
            for (int g = startGroup; g < groups.Count; ++g)
            {
                ListGroup group = groups[g];
                int rpe = Math.Max(1, group.RowsPerEntry);
                int from = (g == startGroup) ? localStart : 0;
                int remaining = Math.Max(0, group.Count - from);

                // Every group's fit test reserves ONE row per later non-empty
                // group (its collapsed line) - the invariant "rowsUsed + later
                // non-empty groups <= BodyBudget" must hold on every branch.
                int laterNonEmpty = 0;
                for (int later = g + 1; later < groups.Count; ++later)
                {
                    if (groups[later].Count > 0) laterNonEmpty++;
                }

                if (group.Count == 0)
                {
                    // An empty group can only appear AFTER startGroup. It is
                    // never truncated: either its 2 rows (header + "(none)")
                    // fit with the later groups' reservations counted too
                    // (log 79), or it is simply omitted - it carries nothing
                    // the caller's own header counts don't already say.
                    if (showEmptyGroups && rowsUsed + 2 + laterNonEmpty <= BodyBudget)
                    {
                        AppendGroupHeader(sb, group, screenWidth);
                        sb.Append(' ', EntryIndent).Append("(none)").Append(NL);
                        rowsUsed += 2;
                    }
                    continue;
                }

                int fullyExpandedRows = 1 /* header */ + remaining * rpe;
                if (rowsUsed + fullyExpandedRows + laterNonEmpty <= BodyBudget)
                {
                    AppendGroupHeader(sb, group, screenWidth);
                    for (int i = from; i < group.Count; ++i) group.RenderEntry(sb, i, screenWidth);
                    rowsUsed += fullyExpandedRows;
                    entriesShown += remaining;
                }
                else
                {
                    int reservedTail = 1 /* header */ + 1 /* +MORE */ + laterNonEmpty;
                    int availableRows = Math.Max(0, BodyBudget - rowsUsed - reservedTail);
                    int shown = Math.Min(availableRows / rpe, remaining);

                    if (shown == 0)
                    {
                        AppendCollapsed(sb, group, remaining, screenWidth);
                        rowsUsed += 1;
                    }
                    else
                    {
                        AppendGroupHeader(sb, group, screenWidth);
                        for (int i = from; i < from + shown; ++i) group.RenderEntry(sb, i, screenWidth);
                        entriesShown += shown;
                        rowsUsed += 1 + shown * rpe;

                        int leftover = remaining - shown; // always > 0 here: a fully-fitting group took the branch above
                        sb.Append(' ', EntryIndent).Append('+').Append(leftover).Append(" MORE").Append(NL);
                        rowsUsed += 1;
                    }

                    for (int later = g + 1; later < groups.Count; ++later)
                    {
                        if (groups[later].Count > 0)
                        {
                            AppendCollapsed(sb, groups[later], groups[later].Count, screenWidth);
                            rowsUsed += 1;
                        }
                    }
                    break;
                }
            }

            return entriesShown;
        }

        private static void AppendGroupHeader(StringBuilder sb, ListGroup group, int screenWidth)
        {
            sb.Append(group.ColorTag);
            AppendWithRight(sb, group.Label, group.HeaderRight, screenWidth);
            sb.Append(ResetColorTag).Append(NL);
        }

        private static void AppendCollapsed(StringBuilder sb, ListGroup group, int count, int screenWidth)
        {
            sb.Append(group.ColorTag);
            AppendWithRight(sb, group.Label + " (" + count + ")", group.HeaderRight, screenWidth);
            sb.Append(ResetColorTag).Append(NL);
        }

        // `left`, then `right` flushed to the screen edge. The left half
        // gives way (truncated) rather than the right one: the right half is
        // a number, and a row longer than the screen would wrap or clip.
        private static void AppendWithRight(StringBuilder sb, string left, string right, int screenWidth)
        {
            if (string.IsNullOrEmpty(right))
            {
                sb.Append(left);
                return;
            }
            int room = screenWidth - right.Length - 1;
            if (room < 0) room = 0;
            if (left.Length > room) left = left.Substring(0, room);
            sb.Append(left).Append(' ', Math.Max(0, screenWidth - left.Length - right.Length)).Append(right);
        }

        // Same fit test AppendBody performs, without rendering: "reaches the
        // end" means every remaining entry, in every group from scrollOffset
        // onward, would print individually - no "+N MORE", no collapsed group.
        private static bool ReachesEnd(IList<ListGroup> groups, int scrollOffset)
        {
            int total = TotalEntries(groups);
            if (total == 0) return true;
            if (scrollOffset >= total - 1) return true;

            int consumed = 0;
            int rowsNeeded = 0;
            bool reached = false;
            for (int i = 0; i < groups.Count; ++i)
            {
                int count = groups[i].Count;
                int rpe = Math.Max(1, groups[i].RowsPerEntry);
                int groupStart = consumed;
                consumed += count;
                if (count == 0) continue;

                if (!reached)
                {
                    if (scrollOffset >= consumed) continue; // entirely before scrollOffset
                    reached = true;
                    int localStart = Math.Max(0, scrollOffset - groupStart);
                    rowsNeeded += 1 + (count - localStart) * rpe;
                }
                else
                {
                    rowsNeeded += 1 + count * rpe;
                }
            }

            return rowsNeeded <= BodyBudget;
        }

        // "X-Y of N" left, key legend flushed right (two halves, log 77).
        private static void AppendStatusLine(StringBuilder sb, int total, int scrollOffset, int entriesShown, int screenWidth,
                                             string keyLegend)
        {
            int first = scrollOffset + 1;
            int last = scrollOffset + entriesShown;
            string position = first + "-" + last + " of " + total;

            // The legend may carry inline color tags (ELEC's green mode-key
            // glyph, log 88): measure what is VISIBLE, not the raw string. A
            // tag-free legend (CAS) takes exactly the path it always did.
            int gap = screenWidth - position.Length - VisibleLength(keyLegend);
            if (gap > 0)
            {
                sb.Append(position).Append(' ', gap).Append(keyLegend);
                return;
            }
            string line = position + " " + StripColorTags(keyLegend); // overflow: plain text, cut at the edge
            if (line.Length > screenWidth) line = line.Substring(0, screenWidth);
            sb.Append(line);
        }

        // Inline color tags are exactly "[#RRGGBBAA]" (11 chars) everywhere
        // in this project; anything else starting with '[' is literal text.
        private const int ColorTagLength = 11;

        private static bool IsColorTagAt(string text, int index)
        {
            return index + ColorTagLength <= text.Length && text[index] == '[' && text[index + 1] == '#'
                   && text[index + ColorTagLength - 1] == ']';
        }

        public static int VisibleLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int length = 0;
            for (int i = 0; i < text.Length;)
            {
                if (IsColorTagAt(text, i)) i += ColorTagLength;
                else { length++; i++; }
            }
            return length;
        }

        public static string StripColorTags(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("[#", StringComparison.Ordinal) < 0) return text;
            StringBuilder plain = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length;)
            {
                if (IsColorTagAt(text, i)) i += ColorTagLength;
                else plain.Append(text[i++]);
            }
            return plain.ToString();
        }

        // Text that overflows its column scrolls (marquee) instead of being cut
        // short, so the full string is eventually readable (user's request,
        // log 68). Driven by real elapsed seconds (the caller passes
        // Time.realtimeSinceStartup - immune to warp/pause), not by the
        // ~50 Hz textmethod poll. Text that fits is returned unchanged.
        public const float MarqueeCharsPerSecond = 4f;
        public const string MarqueeGap = "   "; // seam between the end and the repeat

        public static string Marquee(string text, int width, double nowSeconds)
        {
            if (width <= 0) return string.Empty;
            if (text.Length <= width) return text;

            string loop = text + MarqueeGap;
            int period = loop.Length;
            int offset = (int)(nowSeconds * MarqueeCharsPerSecond) % period;

            StringBuilder window = new StringBuilder(width);
            for (int i = 0; i < width; ++i)
            {
                window.Append(loop[(offset + i) % period]);
            }
            return window.ToString();
        }
    }
}
