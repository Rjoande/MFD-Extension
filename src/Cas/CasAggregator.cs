using System;
using System.Collections.Generic;
using System.Text;
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

        // MdVTextMesh splits rows on Utility.LineSeparator, which is
        // { Environment.NewLine } - "\r\n" on Windows (verified on the real
        // source, Utility.cs/MdVTextMesh.cs, 2026-08-23). A bare '\n' never
        // splits there: the whole page renders as ONE row with everything past
        // the header clipped off the right edge of the screen - exactly the
        // first in-game test's symptom. Never hardcode '\n' in page text.
        private static readonly string NL = Environment.NewLine;

        // Tier now comes from MFDExtension.Shared (2026-08-24 refactor) -
        // the 4-level scale VVEFIS also uses. CAS simply never emits
        // Indication (an alerting page lists anomalies only); that's this
        // aggregator's own policy, not a smaller scale.
        private readonly struct AlertEntry
        {
            internal readonly Tier Tier;
            internal readonly string Label; // specific fault name when known ("ALTERNATOR", "STALL"...), "FAILURE" as the generic fallback - variable width, see AppendTier
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
        // down. Single-threaded like everything else on the prop.
        private static readonly List<DangItBridge.FailureInfo> dangItBuffer = new List<DangItBridge.FailureInfo>();

        internal static string BuildPage(Vessel vessel, int screenWidth, int screenHeight)
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

            List<AlertEntry> entries = new List<AlertEntry>();
            if (vessel != null)
            {
                foreach (Part part in vessel.Parts)
                {
                    CollectDangIt(part, entries);
                    CollectFar(part, entries);
                    CollectRealBattery(part, entries);
                }
            }

            if (entries.Count == 0)
            {
                return NominalPage(screenWidth);
            }

            int warnCount = 0, cautionCount = 0, advisoryCount = 0;
            foreach (AlertEntry e in entries)
            {
                if (e.Tier == Tier.Warning) warnCount++;
                else if (e.Tier == Tier.Caution) cautionCount++;
                else advisoryCount++;
            }

            StringBuilder sb = new StringBuilder();
            AppendHeader(sb, screenWidth, warnCount, cautionCount, advisoryCount);

            AppendTier(sb, entries, Tier.Warning, "WARNING", WarningColorTag, screenWidth);
            AppendTier(sb, entries, Tier.Caution, "CAUTION", CautionColorTag, screenWidth);
            AppendTier(sb, entries, Tier.Advisory, "ADVISORY", AdvisoryColorTag, screenWidth);

            return sb.ToString();
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

        private static void AppendTier(StringBuilder sb, List<AlertEntry> entries, Tier tier, string label, string colorTag, int screenWidth)
        {
            sb.Append(colorTag).Append(label).Append(ResetColorTag).Append(NL);

            const int indent = 2;

            bool any = false;
            foreach (AlertEntry e in entries)
            {
                if (e.Tier != tier) continue;
                any = true;
                // Label is variable width now (fault name, not a fixed
                // "FAILURE"/"STALL" tag), so the title's truncation budget is
                // computed per entry instead of once per tier.
                int titleBudget = screenWidth - indent - e.Label.Length - 1;
                sb.Append(' ', indent).Append(e.Label).Append(' ')
                  .Append(Truncate(e.PartTitle, titleBudget)).Append(NL);
            }

            if (!any)
            {
                sb.Append(' ', indent).Append("(none)").Append(NL);
            }
        }

        private static string Truncate(string text, int maxChars)
        {
            if (maxChars <= 0) return string.Empty;
            if (text.Length <= maxChars) return text;
            if (maxChars == 1) return text.Substring(0, 1);
            return text.Substring(0, maxChars - 1) + "…"; // single ellipsis glyph, same convention as KRILL's label truncation - never three literal dots
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
