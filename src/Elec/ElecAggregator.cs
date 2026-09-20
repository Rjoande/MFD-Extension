using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MFDExtension.Pages;
using MFDExtension.Shared;
using UnityEngine;

namespace MFDExtension.Elec
{
    // Which half of the ledger a list page shows.
    internal enum ElecSide
    {
        Sources,
        Loads,
    }

    // Builds the three ELEC pages (bay R3) from DbsReader's snapshot:
    //   L1 MFDExt_ELEC          - ELEC SUMMARY, static
    //   L2 MFDExt_ELEC_Sources  - per-category list of what feeds the bus
    //   L3 MFDExt_ELEC_Loads    - per-category list of what draws from it
    //
    // TWO LEDGER MODES, switched live with the monitor's ENTER key (the green
    // left arrow, softkey id 2); per monitor, not persisted:
    //  - PLANT (default): the DBS "Batteries" category (RealBattery packs,
    //    NFE discharge capacitors) is kept OUT of NET FLOW / GENERATED /
    //    CONSUMED and shown on its own STORAGE row. Why this is the default:
    //    DBS books a charging RealBattery as a consumer and a discharging one
    //    as a producer, so with RealBattery installed DBS's own net figure
    //    hovers around zero by construction (storage always absorbs or
    //    supplies the balance) and says nothing about the plant.
    //  - TOTAL: every handler counted, storage included - exactly the numbers
    //    DBS's own Systems Monitor window shows.
    // With no storage handler on the vessel the two modes are the same
    // numbers, and neither the mode tag nor the key hint is shown.
    //
    // Boundary with the BMS bay: with both mods installed EXP TIME lives on
    // BMS only and EC LEVEL on ELEC only, so ELEC hides EXP TIME as soon as
    // RealBattery is detected. Colors never depend on what is installed.
    internal static class ElecAggregator
    {
        private const string GreenTag = "[#00FF00FF]";
        private const string AmberTag = "[#FFBF00FF]";   // same amber as CAS CAUTION
        private const string RedTag = "[#BF2626FF]";     // same red as CAS WARNING
        private const string HeaderTag = "[#CCCCCCFF]";  // category headers: a step dimmer than the white entries under them
        private const string DimTag = "[#888888FF]";     // the storage group when it is excluded from the totals, mode tag
        private const string ResetTag = ScrollingListPage.ResetColorTag;

        // EC LEVEL thresholds: RealBattery's RESERVE ones (one key away on
        // the BMS bay), not DBS's own 25 %.
        private const double EcCautionFraction = 0.10;
        private const double EcWarningFraction = 0.01;

        private const int BarCells = 20;
        private const int LabelWidth = 11;
        private const int ValueWidth = 7;        // "9999.99"
        private const int TopLoads = 3;
        private const int TopSources = 2;

        // The mode key's hint: a green U+2190 arrow, matching the green arrow
        // drawn on the physical ENTER key. Its color tags make the raw string
        // longer than what shows - always measure it with VisibleLength.
        private const string ModeLegend = GreenTag + "\u2190" + ResetTag + ": mode";
        private const string ListLegendWithMode = ModeLegend + "  " + ScrollingListPage.KeyLegend;

        private static readonly string NL = ScrollingListPage.NL;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- snapshot cache -------------------------------------------------
        // textmethod is polled at ~50 Hz per monitor per page; the reflection
        // pass runs at most every RefreshSeconds, shared by every ELEC monitor
        // of the vessel. Rendering itself is NOT throttled (the marquee needs
        // its own 4 chars/s clock), only the read is.
        private const float RefreshSeconds = 0.25f;
        private static readonly ElecSnapshot snapshot = new ElecSnapshot();
        private static Guid snapshotVessel = Guid.Empty;
        private static float snapshotTime = -1f;
        private static int snapshotVersion;

        private static ElecSnapshot GetSnapshot(Vessel vessel)
        {
            float now = Time.realtimeSinceStartup;
            if (vessel.id != snapshotVessel || snapshotTime < 0f || now - snapshotTime >= RefreshSeconds || now < snapshotTime)
            {
                DbsReader.Read(vessel, snapshot);
                snapshotVessel = vessel.id;
                snapshotTime = now;
                snapshotVersion++;
            }
            return snapshot;
        }

        // ---- list pages -----------------------------------------------------
        // Groups are rebuilt once per snapshot per (side, mode), not per poll.
        private sealed class ListView
        {
            public int Version = -1;
            public readonly List<ListGroup> Groups = new List<ListGroup>();
            public double Total;   // magnitude, storage excluded in PLANT mode
            public int Count;      // entries counted in Total
            public int Idle;
            public readonly TextCache Text = new TextCache();
        }

        // Last rendered string of a page, reused while nothing that feeds it
        // changed: the snapshot (4 Hz), the scroll offset, the screen width
        // and the marquee step (4 chars/s) - so a page is rebuilt a handful
        // of times per second instead of on each ~50 Hz textmethod poll.
        // Single slot: two monitors on the same page at different offsets
        // just rebuild alternately, still correct.
        private sealed class TextCache
        {
            private int version = -1, offset, width, step;
            private string text;

            public string Get(int currentVersion, int currentOffset, int currentWidth, int currentStep)
            {
                return (text != null && version == currentVersion && offset == currentOffset
                        && width == currentWidth && step == currentStep) ? text : null;
            }

            public string Set(int currentVersion, int currentOffset, int currentWidth, int currentStep, string value)
            {
                version = currentVersion;
                offset = currentOffset;
                width = currentWidth;
                step = currentStep;
                text = value;
                return value;
            }
        }

        private static int MarqueeStep()
        {
            return (int)(Time.realtimeSinceStartup * ScrollingListPage.MarqueeCharsPerSecond);
        }

        private static readonly ListView[] views = { new ListView(), new ListView(), new ListView(), new ListView() };

        private static ListView GetView(ElecSnapshot data, ElecSide side, bool totalMode)
        {
            ListView view = views[(side == ElecSide.Sources ? 0 : 2) + (totalMode ? 1 : 0)];
            if (view.Version == snapshotVersion) return view;

            view.Version = snapshotVersion;
            view.Groups.Clear();
            view.Total = 0.0;
            view.Count = 0;
            view.Idle = 0;

            List<ElecCategory> ordered = new List<ElecCategory>(data.Categories);
            ordered.Sort((a, b) =>
            {
                // PLANT mode: the excluded storage group always goes last.
                if (!totalMode && a.IsStorage != b.IsStorage) return a.IsStorage ? 1 : -1;
                double sumA = side == ElecSide.Sources ? a.SourceSum : -a.LoadSum;
                double sumB = side == ElecSide.Sources ? b.SourceSum : -b.LoadSum;
                int bySum = sumB.CompareTo(sumA);
                return bySum != 0 ? bySum : string.CompareOrdinal(a.Title, b.Title);
            });

            for (int i = 0; i < ordered.Count; ++i)
            {
                ElecCategory category = ordered[i];
                List<ElecEntry> entries = side == ElecSide.Sources ? category.Sources : category.Loads;
                double sum = side == ElecSide.Sources ? category.SourceSum : -category.LoadSum;
                bool excluded = category.IsStorage && !totalMode;

                if (!excluded)
                {
                    view.Total += sum;
                    view.Count += entries.Count;
                    view.Idle += side == ElecSide.Sources ? category.IdleSources : category.IdleLoads;
                }
                if (entries.Count == 0) continue; // empty categories are omitted

                List<ElecEntry> captured = entries;
                view.Groups.Add(new ListGroup
                {
                    Label = excluded ? "STORAGE (EXCLUDED)" : category.Title,
                    ColorTag = excluded ? DimTag : HeaderTag,
                    HeaderRight = FormatValue(sum),
                    Count = entries.Count,
                    RenderEntry = (sb, index, width) => RenderEntry(sb, captured[index], width),
                });
            }
            return view;
        }

        internal static string BuildListPage(Vessel vessel, ElecSide side, bool totalMode, int screenWidth, ref int scrollOffset)
        {
            string title = side == ElecSide.Sources ? "ELEC SOURCES" : "ELEC LOADS";
            string fallback = UnavailablePage(vessel, title, screenWidth);
            if (fallback != null) return fallback;

            ElecSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return NoDataPage(title, screenWidth);

            ListView view = GetView(data, side, totalMode);
            int step = MarqueeStep();
            string cached = view.Text.Get(snapshotVersion, scrollOffset, screenWidth, step);
            if (cached != null) return cached;

            bool hasStorage = HasStorage(data);

            StringBuilder sb = new StringBuilder(1024);
            string counts = FormatValue(view.Total).TrimStart() + " Ec/s " + view.Count + (side == ElecSide.Sources ? " SRC" : " LD");
            if (view.Idle > 0) counts += " +" + view.Idle + " IDLE";
            if (title.Length + 1 + counts.Length > screenWidth) title = side == ElecSide.Sources ? "SOURCES" : "LOADS";
            AppendSplitRow(sb, title, counts, screenWidth);
            sb.Append('-', screenWidth).Append(NL);

            if (view.Groups.Count == 0)
            {
                // Nothing to scroll: same 20-row frame, written by hand.
                sb.Append(NL);
                sb.Append(' ', ScrollingListPage.EntryIndent)
                  .Append(side == ElecSide.Sources ? "(no active sources)" : "(no active loads)").Append(NL);
                for (int i = 2; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
                sb.Append('-', screenWidth).Append(NL);
                AppendSplitRow(sb, string.Empty, hasStorage ? ModeLegend : string.Empty, screenWidth, false);
                scrollOffset = 0;
                return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
            }

            // May clamp scrollOffset; the cache is keyed on the clamped value,
            // which is what the module holds from the next poll on.
            ScrollingListPage.AppendBodyAndStatus(sb, view.Groups, ref scrollOffset, screenWidth, false,
                                                  hasStorage ? ListLegendWithMode : ScrollingListPage.KeyLegend);
            return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
        }

        internal static void TryScrollDown(Vessel vessel, ElecSide side, bool totalMode, ref int scrollOffset)
        {
            if (vessel == null || !DbsReader.IsAvailable) return;
            ElecSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return;
            ScrollingListPage.TryScrollDown(GetView(data, side, totalMode).Groups, ref scrollOffset);
        }

        //   "   4.10  OX-STAT-XL Photovoltaic Pan"  - 2 indent + 7 value + 2 + title (marquee)
        private static void RenderEntry(StringBuilder sb, ElecEntry entry, int screenWidth)
        {
            int titleBudget = screenWidth - ScrollingListPage.EntryIndent - ValueWidth - 2;
            sb.Append(' ', ScrollingListPage.EntryIndent)
              .Append(FormatValue(Math.Abs(entry.Value)))
              .Append("  ")
              .Append(ScrollingListPage.Marquee(entry.Title, titleBudget, Time.realtimeSinceStartup))
              .Append(NL);
        }

        // ---- summary page ---------------------------------------------------
        private static readonly List<ElecEntry> topBuffer = new List<ElecEntry>();
        private static readonly TextCache[] summaryText = { new TextCache(), new TextCache() }; // [PLANT, TOTAL]

        internal static string BuildSummaryPage(Vessel vessel, bool totalMode, int screenWidth)
        {
            const string title = "ELEC SUMMARY";
            string fallback = UnavailablePage(vessel, title, screenWidth);
            if (fallback != null) return fallback;

            ElecSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return NoDataPage(title, screenWidth);

            TextCache cache = summaryText[totalMode ? 1 : 0];
            int step = MarqueeStep();
            string cached = cache.Get(snapshotVersion, 0, screenWidth, step);
            if (cached != null) return cached;

            bool hasStorage = HasStorage(data);

            double generated = 0.0, consumed = 0.0, storageNet = 0.0, netAll = 0.0;
            int sourceCount = 0, loadCount = 0, storageCount = 0;
            for (int i = 0; i < data.Categories.Count; ++i)
            {
                ElecCategory category = data.Categories[i];
                netAll += category.SourceSum + category.LoadSum;
                if (category.IsStorage)
                {
                    storageNet += category.SourceSum + category.LoadSum;
                    storageCount += category.Sources.Count + category.Loads.Count + category.IdleSources + category.IdleLoads;
                    if (!totalMode) continue;
                }
                generated += category.SourceSum;
                consumed -= category.LoadSum;
                sourceCount += category.Sources.Count;
                loadCount += category.Loads.Count;
            }
            double net = generated - consumed;

            StringBuilder sb = new StringBuilder(1024);
            string vesselName = vessel.vesselName ?? string.Empty;
            int nameBudget = screenWidth - title.Length - 3;
            if (nameBudget < 0) nameBudget = 0;
            if (vesselName.Length > nameBudget) vesselName = vesselName.Substring(0, nameBudget);
            AppendSplitRow(sb, title, vesselName, screenWidth);
            sb.Append('-', screenWidth).Append(NL);

            int rows = 0;

            // NET FLOW: DBS's own code - arrow + value, green >= 0, amber < 0,
            // never red, and deliberately NO charge/discharge wording (that is
            // RealBattery's vocabulary for a different quantity).
            bool deficit = net < -DbsReader.IdleThreshold;
            AppendFlowRow(sb, "NET FLOW", deficit ? AmberTag : GreenTag, ArrowFor(net), Math.Abs(net),
                          hasStorage ? (totalMode ? "TOTAL" : "PLANT") : string.Empty, DimTag, screenWidth);
            rows++;
            AppendFlowRow(sb, "GENERATED", null, ' ', generated, sourceCount + " SRC", null, screenWidth);
            rows++;
            AppendFlowRow(sb, "CONSUMED", null, ' ', consumed, loadCount + " LOADS", null, screenWidth);
            rows++;
            if (hasStorage && !totalMode)
            {
                // Arrow from the BUS's point of view, like every other row:
                // up = storage is feeding the bus, down = storage is drawing.
                AppendFlowRow(sb, "STORAGE", null, ArrowFor(storageNet), Math.Abs(storageNet), storageCount + " BAT", null, screenWidth);
                rows++;
            }

            rows += AppendEcLevel(sb, data);

            if (!RealBatteryPresent)
            {
                // DBS's own linear estimate on the whole ledger (always the
                // TOTAL net, whatever the display mode): EC / -net to empty,
                // (max - EC) / net to full.
                sb.Append(ExpTimeRow(data, netAll)).Append(NL);
                rows++;
            }

            sb.Append(BufferRow(data)).Append(NL);
            rows++;

            sb.Append(NL);
            rows++;

            rows += AppendTop(sb, data, ElecSide.Loads, totalMode, TopLoads, "TOP LOADS", screenWidth);
            rows += AppendTop(sb, data, ElecSide.Sources, totalMode, TopSources, "TOP SOURCES", screenWidth);

            for (int i = rows; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
            sb.Append('-', screenWidth).Append(NL);
            AppendSplitRow(sb, string.Empty, hasStorage ? ModeLegend : string.Empty, screenWidth, false);
            return cache.Set(snapshotVersion, 0, screenWidth, step, sb.ToString());
        }

        private static bool HasStorage(ElecSnapshot data)
        {
            for (int i = 0; i < data.Categories.Count; ++i)
            {
                if (data.Categories[i].IsStorage) return true;
            }
            return false;
        }

        private static char ArrowFor(double value)
        {
            if (value > DbsReader.IdleThreshold) return '▲';
            if (value < -DbsReader.IdleThreshold) return '▼'; // ▼
            return ' ';
        }

        // "LABEL      ▲   12.40 Ec/s          RIGHT" - label column, arrow slot
        // (blank on rows that have none, so the numbers line up), value, unit,
        // then `right` flushed to the edge.
        private static void AppendFlowRow(StringBuilder sb, string label, string valueColor, char arrow, double magnitude,
                                          string right, string rightColor, int screenWidth)
        {
            string left = label.PadRight(LabelWidth);
            string value = arrow + " " + FormatValue(magnitude) + " Ec/s";
            int gap = screenWidth - left.Length - value.Length - right.Length;
            if (gap < 1)
            {
                right = string.Empty;
                gap = 0;
            }

            sb.Append(left);
            if (valueColor != null) sb.Append(valueColor);
            sb.Append(value);
            if (valueColor != null) sb.Append(ResetTag);
            sb.Append(' ', gap);
            if (right.Length > 0)
            {
                if (rightColor != null) sb.Append(rightColor);
                sb.Append(right);
                if (rightColor != null) sb.Append(ResetTag);
            }
            sb.Append(NL);
        }

        private static int AppendEcLevel(StringBuilder sb, ElecSnapshot data)
        {
            string label = "EC LEVEL".PadRight(LabelWidth);
            if (data.MaxEc <= 0.0)
            {
                sb.Append(label).Append(DimTag).Append("no storage").Append(ResetTag).Append(NL);
                return 1;
            }

            double fraction = data.Ec / data.MaxEc;
            if (fraction < 0.0) fraction = 0.0;
            if (fraction > 1.0) fraction = 1.0;
            string color = fraction <= EcWarningFraction ? RedTag : fraction < EcCautionFraction ? AmberTag : GreenTag;

            int filled = (int)Math.Round(fraction * BarCells, MidpointRounding.AwayFromZero);
            if (filled == 0 && fraction > EcWarningFraction) filled = 1; // a non-empty battery never draws as an empty bar
            string percent = ((int)Math.Round(fraction * 100.0)).ToString(Inv).PadLeft(3) + "%";

            sb.Append(label).Append('[').Append(color)
              .Append('█', filled).Append('░', BarCells - filled)   // same two glyphs RealBattery's own BMS bars use on this monitor
              .Append(ResetTag).Append("] ").Append(color).Append(percent).Append(ResetTag).Append(NL);

            sb.Append(' ', LabelWidth)
              .Append(data.Ec.ToString("F0", Inv)).Append(" / ").Append(data.MaxEc.ToString("F0", Inv)).Append(" Ec").Append(NL);
            return 2;
        }

        private static string ExpTimeRow(ElecSnapshot data, double netAll)
        {
            if (data.MaxEc <= 0.0) return "EXP TIME".PadRight(LabelWidth) + "--";
            if (netAll < -DbsReader.IdleThreshold)
            {
                return "EXP TIME".PadRight(LabelWidth) + FormatDuration(data.Ec / -netAll);
            }
            if (netAll > DbsReader.IdleThreshold && data.Ec < data.MaxEc - 0.5)
            {
                return "FULL IN".PadRight(LabelWidth) + FormatDuration((data.MaxEc - data.Ec) / netAll);
            }
            return "EXP TIME".PadRight(LabelWidth) + "--";
        }

        private static string BufferRow(ElecSnapshot data)
        {
            string label = "BUFFER".PadRight(LabelWidth);
            switch (data.Buffer)
            {
                case DbsBufferState.Active:
                    return label + GreenTag + "ACTIVE" + ResetTag + "  +" + data.BufferSize.ToString("F0", Inv) + " Ec";
                case DbsBufferState.Off:
                    return label + "OFF  (warp < " + data.TimeWarpLimit.ToString("F0", Inv) + "x)";
                case DbsBufferState.Disabled:
                    return label + DimTag + "n/a  (DBS disabled)" + ResetTag;
                default:
                    return label + DimTag + "n/a" + ResetTag;
            }
        }

        private static int AppendTop(StringBuilder sb, ElecSnapshot data, ElecSide side, bool totalMode, int limit,
                                     string heading, int screenWidth)
        {
            topBuffer.Clear();
            for (int i = 0; i < data.Categories.Count; ++i)
            {
                ElecCategory category = data.Categories[i];
                if (category.IsStorage && !totalMode) continue;
                topBuffer.AddRange(side == ElecSide.Sources ? category.Sources : category.Loads);
            }
            topBuffer.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));

            sb.Append(HeaderTag).Append(heading).Append(ResetTag).Append(NL);
            if (topBuffer.Count == 0)
            {
                sb.Append(' ', ScrollingListPage.EntryIndent).Append("(none)").Append(NL);
                return 2;
            }
            int shown = Math.Min(limit, topBuffer.Count);
            for (int i = 0; i < shown; ++i) RenderEntry(sb, topBuffer[i], screenWidth);
            return 1 + shown;
        }

        // ---- shared helpers ---------------------------------------------------
        private static bool? realBatteryPresent;
        private static bool RealBatteryPresent
        {
            get
            {
                if (realBatteryPresent == null) realBatteryPresent = ModPresence.IsLoaded("RealBattery");
                return realBatteryPresent.Value;
            }
        }

        // Right-aligned in ValueWidth; InvariantCulture always (an Italian
        // install would otherwise print "12,40").
        private static string FormatValue(double value)
        {
            string text = Math.Abs(value) < 9999.995 ? value.ToString("F2", Inv) : value.ToString("F0", Inv);
            return text.PadLeft(ValueWidth);
        }

        // Two most significant units, in the game's own calendar (Kerbin 6 h
        // days or Earth 24 h, whatever the player set - same source DBS uses).
        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0) return "--";

            double yearLength = KSPUtil.dateTimeFormatter.Year;
            double dayLength = KSPUtil.dateTimeFormatter.Day;
            if (seconds >= yearLength * 100.0) return "> 99y";

            long total = (long)seconds;
            long years = (long)(total / yearLength);
            total -= (long)(years * yearLength);
            long days = (long)(total / dayLength);
            total -= (long)(days * dayLength);
            long hours = total / 3600;
            total -= hours * 3600;
            long minutes = total / 60;
            long secs = total - minutes * 60;

            if (years > 0) return years + "y " + days + "d";
            if (days > 0) return days + "d " + hours + "h";
            if (hours > 0) return hours + "h " + minutes.ToString("00", Inv) + "m";
            if (minutes > 0) return minutes + "m " + secs.ToString("00", Inv) + "s";
            return secs + "s";
        }

        // `right` may carry color tags (the mode legend); `left` never does.
        private static void AppendSplitRow(StringBuilder sb, string left, string right, int screenWidth, bool newLine = true)
        {
            int rightWidth = ScrollingListPage.VisibleLength(right);
            int gap = screenWidth - left.Length - rightWidth;
            if (gap < 1 && rightWidth > 0 && left.Length > 0)
            {
                int room = Math.Max(0, screenWidth - rightWidth - 1);
                if (left.Length > room) left = left.Substring(0, room);
                gap = screenWidth - left.Length - rightWidth;
            }
            sb.Append(left).Append(' ', Math.Max(0, gap)).Append(right);
            if (newLine) sb.Append(NL);
        }

        // null = DBS is installed and the vessel is valid, carry on.
        private static string UnavailablePage(Vessel vessel, string title, int screenWidth)
        {
            if (DbsReader.IsAvailable) return vessel == null ? NoDataPage(title, screenWidth) : null;

            StringBuilder sb = new StringBuilder();
            sb.Append(title).Append(NL);
            sb.Append('-', screenWidth).Append(NL);
            sb.Append(NL);
            sb.Append("DYNAMICBATTERYSTORAGE NOT DETECTED").Append(NL);
            sb.Append(NL);
            sb.Append("Install DynamicBatteryStorage").Append(NL);
            sb.Append("(Systems Monitor) for the vessel's").Append(NL);
            sb.Append("electrical ledger.");
            return sb.ToString();
        }

        private static string NoDataPage(string title, int screenWidth)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(title).Append(NL);
            sb.Append('-', screenWidth).Append(NL);
            sb.Append(NL);
            sb.Append("NO DATA").Append(NL);
            sb.Append(NL);
            sb.Append("Waiting for DynamicBatteryStorage").Append(NL);
            sb.Append("to build this vessel's data.");
            return sb.ToString();
        }
    }
}
