using System;
using System.Collections.Generic;
using System.Text;

namespace MFDExtension.Pages
{
    // One group of a scrollable list page: a colored header line plus N
    // entries of a fixed height, rendered on demand by the owner. The engine
    // below doesn't care what an entry is, only how many rows it takes.
    internal sealed class ListGroup
    {
        public string Label;                 // header text, uppercase by convention ("WARNING", "SOLAR PANELS", "LOOP 0")
        public string ColorTag;              // "[#RRGGBBAA]" applied to the header and to the collapsed "LABEL (N)" line
        // Optional text flushed right on the header line AND on the collapsed
        // "LABEL (N)" line (ELEC uses it for a category subtotal); null leaves
        // the output identical to a page that never sets it.
        public string HeaderRight;
        public int Count;                    // number of entries currently in the group
        public int RowsPerEntry = 1;         // every entry of a group has the same height
        // Appends entry `index` to `sb` as EXACTLY RowsPerEntry rows, each
        // terminated by ScrollingListPage.NL. Arguments: (sb, index, screenWidth).
        public Action<StringBuilder, int, int> RenderEntry;
    }

    // One key hint on the status line. A page binds more keys than fit in 40
    // columns, so hints are declared in reading order and dropped by
    // Priority (highest value first, rightmost on a tie) until the line fits.
    internal struct KeyHint
    {
        public string Text;  // may carry inline color tags, always measured with VisibleLength
        public int Priority; // lower survives longer

        public KeyHint(string text, int priority)
        {
            Text = text;
            Priority = priority;
        }
    }

    // The scroll/collapse engine shared by the CAS, ELEC and TCS bays: the fit
    // test, "+N MORE" truncation, a collapsed "LABEL (N)" line for groups not
    // yet reached, and the refusal to scroll past the last full screen.
    // Entries may span several rows; showing empty groups is a caller policy.
    //
    // Deliberately UnityEngine-free so it also builds in a console harness:
    // the marquee takes the clock as an argument.
    //
    // Screen model: this prop is really 40x20 (not the (40,32) MASPageText
    // always passes) and MAS silently drops rows past the 20th, which is why
    // rows are budgeted at all. The caller writes HeaderRows rows, then
    // AppendBodyAndStatus writes the remaining 18, always.
    internal static class ScrollingListPage
    {
        public const int VisibleRows = 20;
        public const int HeaderRows = 2;          // title line + dashes, written by the caller
        public const int StatusSeparatorRows = 1; // "-----" above the status line, worth one body row
        public const int StatusLineRows = 1;      // unconditional: "X-Y of N" + key legend
        public const int BodyBudget = VisibleRows - HeaderRows - StatusSeparatorRows - StatusLineRows; // 16
        public const int EntryIndent = 2;         // entries, "(none)" and "+N MORE" all indent by this

        public const string ResetColorTag = "[#FFFFFFFF]";

        // Key legend, right-flushed on the status line; both glyphs are
        // confirmed to render in the monitor's font.
        public const string ScrollHint = "▲▼: scroll";
        public const string HomeHint = "○: home";
        // Density keys, shared vocabulary: each hint names what the key does
        // FROM the current view, which is also how a page says which view it
        // is in. RIGHT expands, LEFT compacts.
        public const string ExpandHint = "►: exp";
        public const string CompactHint = "◄: cpt";
        public const string HintSeparator = "  ";
        public const string KeyLegend = ScrollHint + HintSeparator + HomeHint;

        // Drop order when a page binds more keys than the line holds: scrolling
        // is the page's own verb, HOME the one a player can do by scrolling up.
        public const int ScrollPriority = 0;
        public const int DensityPriority = 1;
        public const int ModePriority = 2;
        public const int HomePriority = 3;

        // MdVTextMesh splits rows on Environment.NewLine ONLY: with a bare
        // '\n' the whole page renders as one clipped row, silently. Never
        // hardcode '\n' in page text.
        public static readonly string NL = Environment.NewLine;

        public static int TotalEntries(IList<ListGroup> groups)
        {
            int total = 0;
            for (int i = 0; i < groups.Count; ++i) total += groups[i].Count;
            return total;
        }

        // Guards only against a stale offset after the list shrinks; how far
        // DOWN may go is TryScrollDown's policy.
        public static void ClampOffset(IList<ListGroup> groups, ref int scrollOffset)
        {
            int total = TotalEntries(groups);
            if (total == 0) { scrollOffset = 0; return; }
            if (scrollOffset < 0) scrollOffset = 0;
            if (scrollOffset > total - 1) scrollOffset = total - 1;
        }

        // Writes the body from `scrollOffset` (an index into the flat entry
        // sequence across all groups), pads with blank rows up to BodyBudget so
        // the status line always sits on the LAST visible row, then the rule
        // and that line.
        public static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                               int screenWidth, bool showEmptyGroups)
        {
            AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups, KeyLegend);
        }

        // Same, with a page-specific legend: a bay binding one more key than
        // CAS's scroll/home pair says so on its own status line.
        public static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                               int screenWidth, bool showEmptyGroups, string keyLegend)
        {
            AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups, keyLegend, null);
        }

        // Same, with the legend given as hints the status line may drop: a page
        // binding scroll, home, a mode and a density key wants four hints on a
        // row that holds two or three of them.
        public static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                               int screenWidth, bool showEmptyGroups, IList<KeyHint> hints)
        {
            AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups, null, hints);
        }

        private static void AppendBodyAndStatus(StringBuilder sb, IList<ListGroup> groups, ref int scrollOffset,
                                                int screenWidth, bool showEmptyGroups, string keyLegend, IList<KeyHint> hints)
        {
            ClampOffset(groups, ref scrollOffset);
            int total = TotalEntries(groups);

            int entriesShown = AppendBody(sb, groups, scrollOffset, screenWidth, showEmptyGroups, out int rowsUsed);

            for (int i = rowsUsed; i < BodyBudget; ++i)
            {
                sb.Append(NL);
            }
            sb.Append('-', screenWidth).Append(NL);
            AppendStatusLine(sb, total, scrollOffset, entriesShown, screenWidth, keyLegend, hints);
        }

        // The DOWN button's decision of whether to advance at all. Never pushes
        // the last entry alone to the top of an empty screen: stepping by one
        // and refusing once everything remaining is visible converges on
        // exactly the smallest offset that shows it all.
        public static void TryScrollDown(IList<ListGroup> groups, ref int scrollOffset)
        {
            ClampOffset(groups, ref scrollOffset);
            if (TotalEntries(groups) == 0) return;
            if (ReachesEnd(groups, scrollOffset)) return;
            scrollOffset++;
        }

        // Groups entirely before `scrollOffset` are skipped, those that fit
        // expand, the one where the budget runs out truncates with "+N MORE",
        // and later ones collapse to "LABEL (N)". Returns how many entries
        // were printed individually, for "X-Y of N".
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

                // Every fit test reserves ONE row per later non-empty group:
                // the invariant "rowsUsed + laterNonEmpty <= BodyBudget" must
                // hold on every branch below, or the status line falls off.
                int laterNonEmpty = 0;
                for (int later = g + 1; later < groups.Count; ++later)
                {
                    if (groups[later].Count > 0) laterNonEmpty++;
                }

                if (group.Count == 0)
                {
                    // Never truncated: either both its rows fit, reservations
                    // included, or it is omitted - it says nothing the
                    // caller's own header counts don't already say.
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

                    // Not even one entry fits: collapse to the single line that
                    // was reserved - header + "+N MORE" would cost two.
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

        // `left`, then `right` flushed to the screen edge. The left half gives
        // way when they collide: the right one is a number, and a row wider
        // than the screen would wrap or clip.
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

        // AppendBody's fit test without the rendering: "reaches the end" means
        // every entry left would print individually, no "+N MORE", no collapse.
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

        // "X-Y of N" on the left, key legend flushed right. With `hints` the
        // legend is assembled to fit what the position string leaves free.
        private static void AppendStatusLine(StringBuilder sb, int total, int scrollOffset, int entriesShown, int screenWidth,
                                             string keyLegend, IList<KeyHint> hints)
        {
            int first = scrollOffset + 1;
            int last = scrollOffset + entriesShown;
            string position = first + "-" + last + " of " + total;

            if (hints != null) keyLegend = FitHints(hints, screenWidth - position.Length - 1);

            // The legend may carry inline color tags (ELEC colors its mode
            // key), so measure what is VISIBLE, not the raw string length.
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

        // Joins the hints in the order given, dropping the least important
        // ones until what is left fits `available` visible columns. A key
        // whose hint was dropped still works.
        private static readonly List<KeyHint> fitBuffer = new List<KeyHint>(4);

        public static string FitHints(IList<KeyHint> hints, int available)
        {
            fitBuffer.Clear();
            for (int i = 0; i < hints.Count; ++i)
            {
                if (!string.IsNullOrEmpty(hints[i].Text)) fitBuffer.Add(hints[i]);
            }

            while (fitBuffer.Count > 0 && JoinedLength(fitBuffer) > available)
            {
                int worst = 0;
                for (int i = 1; i < fitBuffer.Count; ++i)
                {
                    if (fitBuffer[i].Priority >= fitBuffer[worst].Priority) worst = i;
                }
                fitBuffer.RemoveAt(worst);
            }
            if (fitBuffer.Count == 0) return string.Empty;

            StringBuilder legend = new StringBuilder(available);
            for (int i = 0; i < fitBuffer.Count; ++i)
            {
                if (i > 0) legend.Append(HintSeparator);
                legend.Append(fitBuffer[i].Text);
            }
            return legend.ToString();
        }

        private static int JoinedLength(IList<KeyHint> hints)
        {
            int length = HintSeparator.Length * (hints.Count - 1);
            for (int i = 0; i < hints.Count; ++i) length += VisibleLength(hints[i].Text);
            return length;
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

        // Text wider than its column scrolls instead of being cut short, so the
        // whole string is eventually readable. Driven by the real-time clock
        // the caller passes in, not by the ~50 Hz poll; text that fits is
        // returned unchanged.
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
