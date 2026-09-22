using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MFDExtension.Pages;
using MFDExtension.Shared;
using UnityEngine;

namespace MFDExtension.Tcs
{
    // Builds the three TCS pages (bay R4, Thermal Control System) from
    // SystemHeatReader's snapshot:
    //   L1 MFDExt_TCS           - TCS SUMMARY, static: totals, one row per loop, reactor/cryo/sink counts
    //   L2 MFDExt_TCS_Loops     - one scrollable group per heat loop, its members as the "overlay" in text
    //   L3 MFDExt_TCS_Reactors  - one 4-row block per reactor (fission, then FFT fusion)
    //
    // Colors follow SystemHeat's OWN code, the same principle ELEC applies to
    // DBS's net flow:
    //  - loop flux text: amber when NetFlux > 0, green otherwise;
    //  - NOMINAL green; HEATING amber (NetFlux > 0.05 with T <= Tnom + 0.5,
    //    the panel's amber pulsing border); OVERTEMP red (T >= Tnom + 0.5, its
    //    red pulsing border); CRITICAL red (T - Tnom >= 500 K, the Engineer's
    //    Report "critical" level);
    //  - reactor core: amber above NominalTemperature (the panel's warning
    //    icon), red above CriticalTemperature (core damage in progress);
    //    INTEG magenta below 100 %, our ADVISORY color.
    // Units: SystemHeat works in kW and prints through its SI helper;
    // flux values here are "nnnn kW" up to 9999, "n.nn MW" above, "n.n kW"
    // below 1 kW, always in a 7-character field.
    internal static class TcsAggregator
    {
        private const string GreenTag = "[#00FF00FF]";
        private const string AmberTag = "[#FFBF00FF]";   // same amber as CAS CAUTION
        private const string RedTag = "[#BF2626FF]";     // same red as CAS WARNING
        private const string MagentaTag = "[#FF00FFFF]"; // same magenta as CAS ADVISORY
        private const string HeaderTag = "[#CCCCCCFF]";  // group headers, a step dimmer than the white entries
        private const string DimTag = "[#888888FF]";     // idle counts, off/hibernating reactors
        private const string ResetTag = ScrollingListPage.ResetColorTag;

        // SystemHeat's own thresholds, see the class comment - shared with
        // CAS's thermal entries through the reader.
        private const float HeatingFlux = SystemHeatReader.HeatingFlux;
        private const float OvertempMargin = SystemHeatReader.OvertempMargin;
        private const float CriticalDelta = SystemHeatReader.CriticalDelta;

        private const int LabelWidth = 11;
        private const int FluxWidth = 7;                // " 150 kW", "1.25 MW"
        private const int StatusWidth = 8;              // "OVERTEMP", "MELTDOWN", "CHARGING"
        private const int ReactorRows = 4;              // title + 3 data rows, no blank: the unindented title separates blocks
        private const int GridValueWidth = 12;          // reactor rows: first value block, "1234/1234 K" plus one separator

        private static readonly string NL = ScrollingListPage.NL;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- snapshot ---------------------------------------------------------
        // The 0.25 s reflection cache lives in SystemHeatReader, shared with
        // CAS; only the rendered-text caches below are this file's own.
        private static SystemHeatSnapshot GetSnapshot(Vessel vessel)
        {
            return SystemHeatReader.GetSnapshot(vessel);
        }

        private static int snapshotVersion
        {
            get { return SystemHeatReader.SnapshotVersion; }
        }

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

        private sealed class ListView
        {
            public int Version = -1;
            public readonly List<ListGroup> Groups = new List<ListGroup>();
            public readonly TextCache Text = new TextCache();
        }

        private static readonly ListView[] loopsViews = { new ListView(), new ListView() }; // [expanded, compact]
        private static readonly ListView reactorsView = new ListView();
        private static readonly TextCache summaryText = new TextCache();

        private static int MarqueeStep()
        {
            return (int)(Time.realtimeSinceStartup * ScrollingListPage.MarqueeCharsPerSecond);
        }

        // ---- loop status --------------------------------------------------------
        private enum LoopStatus { Nominal, Heating, Overtemp, Critical }

        private static LoopStatus StatusOf(HeatLoopInfo loop)
        {
            float delta = loop.Temperature - loop.NominalTemperature;
            if (delta >= CriticalDelta) return LoopStatus.Critical;
            if (delta >= OvertempMargin) return LoopStatus.Overtemp;
            if (loop.NetFlux > HeatingFlux) return LoopStatus.Heating;
            return LoopStatus.Nominal;
        }

        private static string StatusTag(LoopStatus status)
        {
            switch (status)
            {
                case LoopStatus.Heating: return AmberTag;
                case LoopStatus.Overtemp:
                case LoopStatus.Critical: return RedTag;
                default: return GreenTag;
            }
        }

        private static string StatusWord(LoopStatus status)
        {
            switch (status)
            {
                case LoopStatus.Heating: return "HEATING";
                case LoopStatus.Overtemp: return "OVERTEMP";
                case LoopStatus.Critical: return "CRITICAL";
                default: return "NOMINAL";
            }
        }

        private static string FluxTag(float netFlux)
        {
            return netFlux > 0f ? AmberTag : GreenTag;
        }

        // ---- summary page -------------------------------------------------------
        internal static string BuildSummaryPage(Vessel vessel, int screenWidth)
        {
            const string title = "TCS SUMMARY";
            string fallback = UnavailablePage(vessel, title, screenWidth);
            if (fallback != null) return fallback;

            SystemHeatSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return NoDataPage(title, screenWidth);

            int step = MarqueeStep();
            string cached = summaryText.Get(snapshotVersion, 0, screenWidth, step);
            if (cached != null) return cached;

            StringBuilder sb = new StringBuilder(1024);
            string vesselName = vessel.vesselName ?? string.Empty;
            int nameBudget = screenWidth - title.Length - 3;
            if (nameBudget < 0) nameBudget = 0;
            if (vesselName.Length > nameBudget) vesselName = vesselName.Substring(0, nameBudget);
            AppendSplitRow(sb, title, vesselName, screenWidth);
            sb.Append('-', screenWidth).Append(NL);

            if (data.Loops.Count == 0 && data.Reactors.Count == 0)
            {
                sb.Append(NL);
                sb.Append("NO HEAT LOOPS ON THIS VESSEL").Append(NL);
                sb.Append(NL);
                sb.Append("No part carries a SystemHeat module.").Append(NL);
                for (int i = 4; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
                sb.Append('-', screenWidth).Append(NL);
                return summaryText.Set(snapshotVersion, 0, screenWidth, step, sb.ToString());
            }

            // Bottom section first, to know how many rows it takes.
            List<string> bottom = BottomRows(data);

            // Row budget: 2 totals + [blank] + loop header + N loops + [blank]
            // + bottom. Blanks go first when it doesn't fit, then the loop
            // table truncates with a "+N MORE" row of its own.
            int loops = data.Loops.Count;
            int fixedRows = 2 + 1 + bottom.Count;
            int blanks = 2;
            int available = ScrollingListPage.BodyBudget - fixedRows - blanks;
            if (loops > available)
            {
                blanks = 0;
                available = ScrollingListPage.BodyBudget - fixedRows;
            }
            int shownLoops = loops;
            bool truncated = false;
            if (loops > available)
            {
                truncated = true;
                shownLoops = Math.Max(0, available - 1);
            }

            int rows = 0;
            float net = data.Generated - data.Rejected;
            AppendFlowRow(sb, "GENERATED", null, data.Generated > 0f ? '▲' : ' ', data.Generated,
                          "LOOPS  " + loops.ToString(Inv), null, screenWidth);
            rows++;
            AppendFlowRow(sb, "REJECTED", null, data.Rejected > 0f ? '▼' : ' ', data.Rejected,
                          "NET " + ArrowFor(net) + " " + FormatFlux(Math.Abs(net)).TrimStart(),
                          net > HeatingFlux ? AmberTag : GreenTag, screenWidth);
            rows++;
            if (blanks > 0) { sb.Append(NL); rows++; }

            // Columns: id 0-2, T 5-9, '/', Tnom 11-14, arrow 18, flux 20-26, status 30-37.
            sb.Append(HeaderTag).Append("LOOP TEMP/NOM K     FLUX      STATUS").Append(ResetTag).Append(NL);
            rows++;
            for (int i = 0; i < shownLoops; ++i)
            {
                HeatLoopInfo loop = data.Loops[i];
                LoopStatus status = StatusOf(loop);
                sb.Append(loop.Id.ToString(Inv).PadLeft(3)).Append("  ")
                  .Append(StatusTag(status))
                  .Append(loop.Temperature.ToString("F0", Inv).PadLeft(5)).Append('/')
                  .Append(loop.NominalTemperature.ToString("F0", Inv).PadLeft(4))
                  .Append(ResetTag).Append("   ")
                  .Append(FluxTag(loop.NetFlux)).Append(ArrowFor(loop.NetFlux)).Append(' ')
                  .Append(FormatFlux(Math.Abs(loop.NetFlux))).Append(ResetTag).Append("   ")
                  .Append(StatusTag(status)).Append(StatusWord(status)).Append(ResetTag).Append(NL);
                rows++;
            }
            if (truncated)
            {
                sb.Append(' ', ScrollingListPage.EntryIndent).Append('+').Append(loops - shownLoops).Append(" MORE (see LOOPS)").Append(NL);
                rows++;
            }
            if (blanks > 1) { sb.Append(NL); rows++; }

            for (int i = 0; i < bottom.Count; ++i)
            {
                sb.Append(bottom[i]).Append(NL);
                rows++;
            }

            for (int i = rows; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
            sb.Append('-', screenWidth).Append(NL);
            // no key does anything on this page: empty status line, same 20-row frame
            return summaryText.Set(snapshotVersion, 0, screenWidth, step, sb.ToString());
        }

        // REACTORS / CRYO / SINKS rows, each only when the vessel has any
        // (same "zero entries hidden" rule as ELEC).
        private static List<string> BottomRows(SystemHeatSnapshot data)
        {
            List<string> rows = new List<string>(3);

            if (data.Reactors.Count > 0)
            {
                int on = 0, hibernating = 0, scram = 0, meltdown = 0, charging = 0;
                for (int i = 0; i < data.Reactors.Count; ++i)
                {
                    switch (data.Reactors[i].State)
                    {
                        case ReactorState.On: on++; break;
                        case ReactorState.Hibernating: hibernating++; break;
                        case ReactorState.Scram: scram++; break;
                        case ReactorState.Meltdown: meltdown++; break;
                        case ReactorState.Charging: charging++; break;
                    }
                }
                StringBuilder row = new StringBuilder();
                row.Append("REACTORS".PadRight(LabelWidth - 1)).Append(data.Reactors.Count.ToString(Inv).PadLeft(2))
                   .Append("   ON ").Append(on.ToString(Inv));
                if (hibernating > 0) row.Append("   HIBERN ").Append(hibernating.ToString(Inv));
                if (charging > 0) row.Append("   CHARGING ").Append(charging.ToString(Inv));
                if (scram > 0) row.Append("   ").Append(RedTag).Append("SCRAM ").Append(scram.ToString(Inv)).Append(ResetTag);
                if (meltdown > 0) row.Append("   ").Append(RedTag).Append("MELTDOWN ").Append(meltdown.ToString(Inv)).Append(ResetTag);
                rows.Add(row.ToString());
            }

            if (data.CryoTanks > 0)
            {
                string row = "CRYO".PadRight(LabelWidth - 1) + data.CryoTanks.ToString(Inv).PadLeft(2) + "   ";
                row += data.Boiloff > 0
                    ? AmberTag + "BOILOFF " + data.Boiloff.ToString(Inv) + ResetTag
                    : "BOILOFF 0";
                rows.Add(row);
            }

            if (data.Sinks > 0)
            {
                string stored = data.SinkCapacity > 0f
                    ? ((int)Math.Round(100.0 * data.SinkStored / data.SinkCapacity)).ToString(Inv) + "% stored"
                    : "--";
                rows.Add("SINKS".PadRight(LabelWidth - 1) + data.Sinks.ToString(Inv).PadLeft(2) + "   " + stored);
            }

            return rows;
        }

        // ---- loops page ---------------------------------------------------------
        // Members folded by part in the compact view: one list per loop, reused
        // across rebuilds and kept alive by the render closures below.
        private static readonly List<List<CollapsedEntry<HeatMember>>> compactMembers =
            new List<List<CollapsedEntry<HeatMember>>>();

        private static readonly Func<HeatMember, string> MemberKey = member => member.Key;
        private static readonly Func<HeatMember, double> MemberFlux = member => member.Flux;

        // Same ranking SystemHeatReader gives the raw members - sources first,
        // then sinks, each by magnitude - applied to the summed rows.
        private static int CompareCollapsedMembers(CollapsedEntry<HeatMember> a, CollapsedEntry<HeatMember> b)
        {
            bool sourceA = a.Sum > 0.0, sourceB = b.Sum > 0.0;
            if (sourceA != sourceB) return sourceA ? -1 : 1;
            int byValue = Math.Abs(b.Sum).CompareTo(Math.Abs(a.Sum));
            return byValue != 0 ? byValue : string.CompareOrdinal(a.Sample.Title, b.Sample.Title);
        }

        private static List<CollapsedEntry<HeatMember>> MemberBuffer(int index)
        {
            while (compactMembers.Count <= index) compactMembers.Add(new List<CollapsedEntry<HeatMember>>());
            return compactMembers[index];
        }

        private static ListView GetLoopsView(SystemHeatSnapshot data, bool compact)
        {
            ListView view = loopsViews[compact ? 1 : 0];
            if (view.Version == snapshotVersion) return view;
            view.Version = snapshotVersion;
            view.Groups.Clear();

            for (int i = 0; i < data.Loops.Count; ++i)
            {
                HeatLoopInfo loop = data.Loops[i];
                LoopStatus status = StatusOf(loop);
                int idleRow = loop.Idle > 0 ? 1 : 0;

                int count;
                Action<StringBuilder, int, int> render;
                if (compact)
                {
                    List<CollapsedEntry<HeatMember>> members = MemberBuffer(i);
                    EntryCollapser.Collapse(loop.Members, members, MemberKey, MemberFlux, CompareCollapsedMembers);
                    count = members.Count + idleRow;
                    render = (sb, index, width) => RenderCollapsedMember(sb, loop, members, index, width);
                }
                else
                {
                    count = loop.Members.Count + idleRow;
                    render = (sb, index, width) => RenderMember(sb, loop, index, width);
                }

                view.Groups.Add(new ListGroup
                {
                    Label = "LOOP " + loop.Id.ToString(Inv),
                    ColorTag = StatusTag(status),
                    HeaderRight = loop.Temperature.ToString("F0", Inv).PadLeft(4) + "/" + loop.NominalTemperature.ToString("F0", Inv).PadLeft(4)
                                  + " K  " + ArrowFor(loop.NetFlux) + " " + FormatFlux(Math.Abs(loop.NetFlux)),
                    Count = count,
                    RenderEntry = render,
                });
            }
            return view;
        }

        //   "  ▲  900 kW  MX-1 'Garnet' Fission React"  - 2 indent + arrow + 1 + flux 7 + 2 + title (marquee)
        //   "  (+2 idle)"                                - the loop's zero-flux members, counted not listed
        private static void RenderMember(StringBuilder sb, HeatLoopInfo loop, int index, int screenWidth)
        {
            if (index >= loop.Members.Count)
            {
                sb.Append(' ', ScrollingListPage.EntryIndent).Append(DimTag).Append("(+").Append(loop.Idle).Append(" idle)").Append(ResetTag).Append(NL);
                return;
            }
            HeatMember member = loop.Members[index];
            AppendMemberRow(sb, member.Title, member.Flux, 0, screenWidth);
        }

        //   "  ▼  0 kW  (6) Thermal radiator"  - the six identical
        //   members of a symmetry group on one row, flux summed.
        private static void RenderCollapsedMember(StringBuilder sb, HeatLoopInfo loop, List<CollapsedEntry<HeatMember>> members,
                                                  int index, int screenWidth)
        {
            if (index >= members.Count)
            {
                sb.Append(' ', ScrollingListPage.EntryIndent).Append(DimTag).Append("(+").Append(loop.Idle).Append(" idle)").Append(ResetTag).Append(NL);
                return;
            }
            CollapsedEntry<HeatMember> entry = members[index];
            AppendMemberRow(sb, entry.Sample.Title, (float)entry.Sum, entry.Count, screenWidth);
        }

        // The count prefix eats into the title column only, so flux stays in
        // its own field whether the row is folded or not.
        private static void AppendMemberRow(StringBuilder sb, string title, float flux, int count, int screenWidth)
        {
            int titleBudget = screenWidth - ScrollingListPage.EntryIndent - 1 - 1 - FluxWidth - 2
                              - EntryCollapser.CountPrefixWidth(count);
            sb.Append(' ', ScrollingListPage.EntryIndent)
              .Append(flux > 0f ? '▲' : '▼').Append(' ')
              .Append(FormatFlux(Math.Abs(flux)))
              .Append("  ");
            EntryCollapser.AppendCountPrefix(sb, count);
            sb.Append(ScrollingListPage.Marquee(title, titleBudget, Time.realtimeSinceStartup)).Append(NL);
        }

        internal static string BuildLoopsPage(Vessel vessel, bool compact, int screenWidth, ref int scrollOffset)
        {
            const string title = "TCS LOOPS";
            string fallback = UnavailablePage(vessel, title, screenWidth);
            if (fallback != null) return fallback;

            SystemHeatSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return NoDataPage(title, screenWidth);

            ListView view = GetLoopsView(data, compact);
            int step = MarqueeStep();
            string cached = view.Text.Get(snapshotVersion, scrollOffset, screenWidth, step);
            if (cached != null) return cached;

            StringBuilder sb = new StringBuilder(1024);
            float net = data.Generated - data.Rejected;
            string right = data.Loops.Count.ToString(Inv) + (data.Loops.Count == 1 ? " LOOP  " : " LOOPS  ")
                           + ArrowFor(net) + " " + FormatFlux(Math.Abs(net)).TrimStart();
            AppendSplitRow(sb, title, right, screenWidth);
            sb.Append('-', screenWidth).Append(NL);

            if (view.Groups.Count == 0)
            {
                sb.Append(NL);
                sb.Append(' ', ScrollingListPage.EntryIndent).Append("(no heat loops)").Append(NL);
                for (int i = 2; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
                sb.Append('-', screenWidth).Append(NL);
                // no key does anything on this page: empty status line, same 20-row frame
                scrollOffset = 0;
                return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
            }

            ScrollingListPage.AppendBodyAndStatus(sb, view.Groups, ref scrollOffset, screenWidth, false,
                                                  LoopHints(compact));
            return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
        }

        private static readonly List<KeyHint> loopHints = new List<KeyHint>(3);

        private static List<KeyHint> LoopHints(bool compact)
        {
            loopHints.Clear();
            loopHints.Add(new KeyHint(compact ? ScrollingListPage.ExpandHint : ScrollingListPage.CompactHint,
                                      ScrollingListPage.DensityPriority));
            loopHints.Add(new KeyHint(ScrollingListPage.ScrollHint, ScrollingListPage.ScrollPriority));
            loopHints.Add(new KeyHint(ScrollingListPage.HomeHint, ScrollingListPage.HomePriority));
            return loopHints;
        }

        // ---- reactors page ------------------------------------------------------
        private static readonly List<ReactorInfo> fissionBuffer = new List<ReactorInfo>();
        private static readonly List<ReactorInfo> fusionBuffer = new List<ReactorInfo>();

        private static ListView GetReactorsView(SystemHeatSnapshot data)
        {
            ListView view = reactorsView;
            if (view.Version == snapshotVersion) return view;
            view.Version = snapshotVersion;
            view.Groups.Clear();

            fissionBuffer.Clear();
            fusionBuffer.Clear();
            for (int i = 0; i < data.Reactors.Count; ++i)
            {
                (data.Reactors[i].Kind == ReactorKind.Fusion ? fusionBuffer : fissionBuffer).Add(data.Reactors[i]);
            }
            if (fissionBuffer.Count > 0)
            {
                view.Groups.Add(new ListGroup
                {
                    Label = "FISSION",
                    ColorTag = HeaderTag,
                    Count = fissionBuffer.Count,
                    RowsPerEntry = ReactorRows,
                    RenderEntry = (sb, index, width) => RenderReactor(sb, fissionBuffer[index], width),
                });
            }
            if (fusionBuffer.Count > 0)
            {
                view.Groups.Add(new ListGroup
                {
                    Label = "FUSION",
                    ColorTag = HeaderTag,
                    Count = fusionBuffer.Count,
                    RowsPerEntry = ReactorRows,
                    RenderEntry = (sb, index, width) => RenderReactor(sb, fusionBuffer[index], width),
                });
            }
            return view;
        }

        // Exactly ReactorRows rows on a fixed grid, so values line up under
        // values and second labels under second labels:
        //   col 2  label (6 wide)   col 8  value (12 wide)   col 20  label (5)   col 25  value (5)   col 30  INTEG
        //   MX-1 'Garnet' Fission Reactor         ON     title (marquee) + status, right-flushed, colored
        //     PWR   120 Ec/s    HEAT 900 kW
        //     CORE  905/900 K   THR  85%  INTEG 100%    fission, exactly 40 columns even at "1234/1234 K"
        //     LIFE  2y 41d                              fission: FUEL n% when not burning / config unreadable
        //   fusion: row 3 "TEMP  900 K       CHARGE 85%" (outlet temperature), row 4 blank
        private static void RenderReactor(StringBuilder sb, ReactorInfo reactor, int screenWidth)
        {
            string stateWord;
            string stateTag;
            switch (reactor.State)
            {
                case ReactorState.On: stateWord = "ON"; stateTag = GreenTag; break;
                case ReactorState.Hibernating: stateWord = "HIBERN"; stateTag = DimTag; break;
                case ReactorState.Charging: stateWord = "CHARGING"; stateTag = AmberTag; break;
                case ReactorState.Scram: stateWord = "SCRAM"; stateTag = RedTag; break;
                case ReactorState.Meltdown: stateWord = "MELTDOWN"; stateTag = RedTag; break;
                default: stateWord = "OFF"; stateTag = DimTag; break;
            }
            int titleBudget = screenWidth - StatusWidth - 2;
            sb.Append(ScrollingListPage.Marquee(reactor.Title, titleBudget, Time.realtimeSinceStartup).PadRight(titleBudget))
              .Append("  ").Append(stateTag).Append(stateWord.PadLeft(StatusWidth)).Append(ResetTag).Append(NL);

            sb.Append("  PWR   ").Append((reactor.Power.ToString("F0", Inv) + " Ec/s").PadRight(GridValueWidth))
              .Append("HEAT ").Append(FormatFlux(reactor.Heat).TrimStart()).Append(NL);

            if (reactor.Kind == ReactorKind.Fission)
            {
                string coreTag = reactor.CoreTemperature > reactor.CriticalTemperature ? RedTag
                               : reactor.CoreTemperature > reactor.NominalTemperature ? AmberTag : null;
                string core = reactor.CoreTemperature.ToString("F0", Inv);
                string coreBlock = core + "/" + reactor.NominalTemperature.ToString("F0", Inv) + " K";
                sb.Append("  CORE  ");
                if (coreTag != null) sb.Append(coreTag);
                sb.Append(core);
                if (coreTag != null) sb.Append(ResetTag);
                sb.Append(coreBlock.Substring(core.Length).PadRight(Math.Max(0, GridValueWidth - core.Length)))
                  .Append("THR  ").Append((((int)Math.Round(reactor.Throttle)).ToString(Inv) + "%").PadRight(5))
                  .Append("INTEG ");
                bool damaged = reactor.Integrity < 100f;
                if (damaged) sb.Append(MagentaTag);
                sb.Append(((int)Math.Floor(reactor.Integrity)).ToString(Inv).PadLeft(3)).Append('%');
                if (damaged) sb.Append(ResetTag);
                sb.Append(NL);

                if (!double.IsNaN(reactor.LifeSeconds)) sb.Append("  LIFE  ").Append(FormatDuration(reactor.LifeSeconds));
                else if (!double.IsNaN(reactor.FuelFraction)) sb.Append("  FUEL  ").Append(((int)Math.Round(reactor.FuelFraction * 100.0)).ToString(Inv)).Append('%');
                else sb.Append("  FUEL  n/a");
                sb.Append(NL);
            }
            else
            {
                sb.Append("  TEMP  ").Append((reactor.CoreTemperature.ToString("F0", Inv) + " K").PadRight(GridValueWidth));
                if (!float.IsNaN(reactor.ChargeFraction))
                {
                    sb.Append("CHARGE ").Append(((int)Math.Round(Mathf.Clamp01(reactor.ChargeFraction) * 100f)).ToString(Inv)).Append('%');
                }
                sb.Append(NL);
                sb.Append(NL);
            }
        }

        internal static string BuildReactorsPage(Vessel vessel, int screenWidth, ref int scrollOffset)
        {
            const string title = "TCS REACTORS";
            string fallback = UnavailablePage(vessel, title, screenWidth);
            if (fallback != null) return fallback;

            SystemHeatSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return NoDataPage(title, screenWidth);

            ListView view = GetReactorsView(data);
            int step = MarqueeStep();
            string cached = view.Text.Get(snapshotVersion, scrollOffset, screenWidth, step);
            if (cached != null) return cached;

            StringBuilder sb = new StringBuilder(1024);
            string right = data.Reactors.Count.ToString(Inv) + (data.Reactors.Count == 1 ? " UNIT" : " UNITS");
            AppendSplitRow(sb, title, right, screenWidth);
            sb.Append('-', screenWidth).Append(NL);

            if (view.Groups.Count == 0)
            {
                sb.Append(NL);
                sb.Append(' ', ScrollingListPage.EntryIndent).Append("(no reactors)").Append(NL);
                for (int i = 2; i < ScrollingListPage.BodyBudget; ++i) sb.Append(NL);
                sb.Append('-', screenWidth).Append(NL);
                // no key does anything on this page: empty status line, same 20-row frame
                scrollOffset = 0;
                return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
            }

            ScrollingListPage.AppendBodyAndStatus(sb, view.Groups, ref scrollOffset, screenWidth, false);
            return view.Text.Set(snapshotVersion, scrollOffset, screenWidth, step, sb.ToString());
        }

        // ---- scrolling ----------------------------------------------------------
        internal static void TryScrollDown(Vessel vessel, bool reactors, bool compact, ref int scrollOffset)
        {
            if (vessel == null || !SystemHeatReader.IsAvailable) return;
            SystemHeatSnapshot data = GetSnapshot(vessel);
            if (!data.HasData) return;
            ListView view = reactors ? GetReactorsView(data) : GetLoopsView(data, compact);
            ScrollingListPage.TryScrollDown(view.Groups, ref scrollOffset);
        }

        // ---- shared helpers -----------------------------------------------------
        private static char ArrowFor(float value)
        {
            if (value > HeatingFlux) return '▲';
            if (value < -HeatingFlux) return '▼';
            return ' ';
        }

        // 7 characters, right-aligned; magnitudes only, the caller adds the arrow.
        internal static string FormatFlux(float kw)
        {
            if (float.IsNaN(kw) || float.IsInfinity(kw)) return "--".PadLeft(FluxWidth);
            float abs = Math.Abs(kw);
            string text;
            if (abs >= 100000f) text = (kw / 1000f).ToString("F0", Inv) + " MW";      // "100 MW"
            else if (abs >= 10000f) text = (kw / 1000f).ToString("F1", Inv) + " MW";  // "12.5 MW"
            else if (abs >= 1000f) text = (kw / 1000f).ToString("F2", Inv) + " MW";   // "1.25 MW"
            else if (abs >= 1f || abs == 0f) text = kw.ToString("F0", Inv) + " kW";
            else text = kw.ToString("F1", Inv) + " kW";
            return text.PadLeft(FluxWidth);
        }

        // "LABEL      ▲ 1.25 MW           RIGHT" - label column, arrow slot,
        // flux, then `right` flushed to the edge (optionally colored).
        private static void AppendFlowRow(StringBuilder sb, string label, string valueColor, char arrow, float magnitude,
                                          string right, string rightColor, int screenWidth)
        {
            string left = label.PadRight(LabelWidth);
            string value = arrow + " " + FormatFlux(magnitude);
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

        // Two most significant units in the game's own calendar (same helper
        // as ELEC's EXP TIME), compact: "2y 41d", "12d 3h", "> 99y".
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

        // `right` may carry color tags; `left` never does.
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

        // null = SystemHeat is installed and the vessel is valid, carry on.
        private static string UnavailablePage(Vessel vessel, string title, int screenWidth)
        {
            if (SystemHeatReader.IsAvailable) return vessel == null ? NoDataPage(title, screenWidth) : null;

            StringBuilder sb = new StringBuilder();
            sb.Append(title).Append(NL);
            sb.Append('-', screenWidth).Append(NL);
            sb.Append(NL);
            sb.Append("SYSTEMHEAT NOT DETECTED").Append(NL);
            sb.Append(NL);
            sb.Append("Install SystemHeat for the vessel's").Append(NL);
            sb.Append("thermal loops, radiators and").Append(NL);
            sb.Append("reactors.");
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
            sb.Append("Waiting for SystemHeat to build").Append(NL);
            sb.Append("this vessel's heat loops.");
            return sb.ToString();
        }
    }
}
