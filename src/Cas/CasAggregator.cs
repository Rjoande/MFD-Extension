using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MFDExtension.Pages;
using MFDExtension.Shared;

namespace MFDExtension.Cas
{
    // Builds the CAS (Crew Alerting System) fault-summary page.
    //
    // Narrower on purpose than VVEFISSeverity: it reads DangIt failures, FAR
    // stalls, three RealBattery malfunctions and SystemHeat's thermal alerts,
    // never fuel or generic engine condition. An expected condition (an idle
    // engine, a half-full tank, a battery sitting disabled) is not CAS
    // material - only genuine anomalies belong on an alerting page.
    //
    // A part may contribute more than one entry: independent problems are
    // listed separately, not merged into "worst wins" as a fill color is.
    //
    // The page LAYOUT - scroll, per-tier collapse, "+N MORE", the anchored
    // status line, marquee titles - lives in Pages/ScrollingListPage and is
    // shared with the ELEC/TCS bays. This file feeds it three ListGroups.
    internal static class CasAggregator
    {
        // The same WARNING/CAUTION/ADVISORY hues as VVEFISSeverity's palette,
        // so a part reads alike on the 3D view and on this page.
        private const string WarningColorTag = "[#BF2626FF]";
        private const string CautionColorTag = "[#FFBF00FF]";
        private const string AdvisoryColorTag = "[#FF00FFFF]";

        // Fixed, so every row's title starts in the same column instead of
        // drifting with each label's length. 7 sits just above the label
        // median and leaves the title 30 of the 37 usable characters
        // (screenWidth 40 - indent 2 - 1 separator).
        //
        // The cost: 12 of the 25 labels go through LabelAbbreviations below,
        // and on a LOCALIZED install none of its English keys match, so a long
        // label falls back to TruncateLabel's ellipsis.
        private const int LabelColumnWidth = 7;

        // Every label longer than LabelColumnWidth, abbreviated by hand in
        // aviation shorthand rather than cut: ALTR not ALT (which is altitude
        // on a flight deck), CTL SRF not SURFACE (the planetary one), RWA the
        // real initialism. Two marquees on one row would be unreadable.
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

        // CAS never emits Indication: an alerting page lists anomalies only.
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

        // Reused across frames: BuildPage runs on every textmethod poll while
        // the page is up. Everything on the prop is single-threaded, so one
        // set of buffers is safe even with two CAS monitors on the vessel.
        private static readonly List<DangItBridge.FailureInfo> dangItBuffer = new List<DangItBridge.FailureInfo>();
        private static readonly List<AlertEntry> entryBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> warnBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> cautionBuffer = new List<AlertEntry>();
        private static readonly List<AlertEntry> advisoryBuffer = new List<AlertEntry>();

        // The three groups handed to the page engine, in severity order: built
        // once, only their Count changes per frame.
        private static readonly ListGroup[] groups =
        {
            new ListGroup { Label = "WARNING",  ColorTag = WarningColorTag,  RenderEntry = (sb, i, w) => RenderEntry(sb, warnBuffer[i], w) },
            new ListGroup { Label = "CAUTION",  ColorTag = CautionColorTag,  RenderEntry = (sb, i, w) => RenderEntry(sb, cautionBuffer[i], w) },
            new ListGroup { Label = "ADVISORY", ColorTag = AdvisoryColorTag, RenderEntry = (sb, i, w) => RenderEntry(sb, advisoryBuffer[i], w) },
        };

        // `scrollOffset` indexes the flat WARNING-then-CAUTION-then-ADVISORY
        // sequence (what "X-Y of N" counts), owned by the caller and passed by
        // ref so the engine can clamp it when the list shrinks mid-scroll.
        internal static string BuildPage(Vessel vessel, int screenWidth, int screenHeight, ref int scrollOffset)
        {
            // Both CLR names: "DangIt" is the classic DLL, "DangItContinued"
            // the fork - see ModPresence.
            bool dangItLoaded = ModPresence.IsLoaded("DangIt", "DangItContinued");
            bool farLoaded = ModPresence.IsLoaded("FerramAerospaceResearch");
            bool realBatteryLoaded = ModPresence.IsLoaded("RealBattery");
            bool systemHeatLoaded = SystemHeatReader.IsAvailable;

            if (!dangItLoaded && !farLoaded && !realBatteryLoaded && !systemHeatLoaded)
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
            // CAS prints empty tiers as "(none)"; the ELEC/TCS bays omit them.
            ScrollingListPage.AppendBodyAndStatus(sb, groups, ref scrollOffset, screenWidth, showEmptyGroups: true);
            return sb.ToString();
        }

        // The DOWN button. Re-collecting here costs nothing: this runs once
        // per physical key press, while BuildPage re-collects on every poll.
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
                CollectSystemHeat(vessel, entryBuffer);
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
                Tier tier = DangItBridge.MapPriorityToTier(failure.Priority);

                // The specific fault name leads the line, so two failures on
                // one part don't read as two identical rows; uppercased to
                // match the headers, "FAILURE" when ScreenName is unreadable.
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

        // Same fixed precedence as VVEFISSeverity (Runaway, Overheat, EOL;
        // first match wins), narrowed to genuine malfunctions. BatteryDisabled
        // and SC_SOC are never read here, so an EOL cell that later gets
        // disabled stays listed: EOL is a standing capacity fact.
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

        // Thermal entries from SystemHeat's shared snapshot, cached in the
        // reader so a ~50 Hz poll costs nothing. Every label fits
        // LabelColumnWidth. A loop has no part, so its "title" carries the
        // loop id and the temperature pair instead.
        private static void CollectSystemHeat(Vessel vessel, List<AlertEntry> entries)
        {
            if (!SystemHeatReader.IsAvailable) return;
            SystemHeatSnapshot data = SystemHeatReader.GetSnapshot(vessel);
            if (!data.HasData) return;

            for (int i = 0; i < data.Loops.Count; ++i)
            {
                HeatLoopInfo loop = data.Loops[i];
                float delta = loop.Temperature - loop.NominalTemperature;
                if (delta < SystemHeatReader.OvertempMargin) continue;
                Tier tier = delta >= SystemHeatReader.CriticalDelta ? Tier.Warning : Tier.Caution;
                string title = "Heat loop " + loop.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                               + "  " + loop.Temperature.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                               + "/" + loop.NominalTemperature.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " K";
                entries.Add(new AlertEntry(tier, "OVHT", title));
            }

            for (int i = 0; i < data.Reactors.Count; ++i)
            {
                ReactorInfo reactor = data.Reactors[i];
                switch (reactor.State)
                {
                    case ReactorState.Meltdown:
                        entries.Add(new AlertEntry(Tier.Warning, "MELTDWN", reactor.Title));
                        continue; // nothing else matters on a dead core
                    case ReactorState.Scram:
                        entries.Add(new AlertEntry(Tier.Warning, "SCRAM", reactor.Title));
                        break;
                }
                if (reactor.Kind != ReactorKind.Fission) continue; // fusion: no core model to judge

                if (reactor.CoreTemperature > reactor.CriticalTemperature)
                    entries.Add(new AlertEntry(Tier.Warning, "CORE", reactor.Title));
                else if (reactor.CoreTemperature > reactor.NominalTemperature)
                    entries.Add(new AlertEntry(Tier.Caution, "CORE", reactor.Title));

                if (reactor.Integrity < 100f)
                    entries.Add(new AlertEntry(Tier.Advisory, "INTEG", reactor.Title));
            }

            for (int i = 0; i < data.BoiloffTanks.Count; ++i)
            {
                entries.Add(new AlertEntry(Tier.Advisory, "BOILOFF", data.BoiloffTanks[i]));
            }
        }

        private static void AppendHeader(StringBuilder sb, int screenWidth, int warnCount, int cautionCount, int advisoryCount)
        {
            string counts = "W:" + warnCount + " C:" + cautionCount + " A:" + advisoryCount;
            int padWidth = screenWidth - counts.Length;
            sb.Append(padWidth > 0 ? "FAULT SUMMARY".PadRight(padWidth) : "FAULT SUMMARY").Append(counts).Append(NL);
            sb.Append(new string('-', screenWidth)).Append(NL);
        }

        // One entry = one row: fixed-width label column, then the part title
        // scrolling in what is left. TruncateLabel runs here rather than at
        // collection time, so it covers every source from one place.
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
            sb.Append("Install DangIt, FAR, RealBattery").Append(NL);
            sb.Append("or SystemHeat for active fault").Append(NL);
            sb.Append("monitoring.");
            return sb.ToString();
        }
    }
}
