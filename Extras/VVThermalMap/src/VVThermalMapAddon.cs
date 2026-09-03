using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVThermalMap
{
    // Registers a new VesselView custom color mode ("SHELL TEMP") using the
    // same public extension points VVEFISAddon uses (CustomModeSettings +
    // VViewCustomMenusMenu.registerMenu / VesselViewPlugin.registerCustomMode) -
    // purely additive, installs alongside VesselView/VesselViewRPM without
    // patching or replacing any of its files. See MFDExtension/CLAUDE.md and
    // Extras/VVThermalMap/CLAUDE.md for the full design history.
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVThermalMapAddon : MonoBehaviour
    {
        // In-flight wireframe toggle (2026-09-01), ported verbatim from
        // VVEFISAddon after it was implemented and tested there first (see
        // MFDExtension/CLAUDE.md log 76). Global to the process, not
        // per-screen - every VV monitor running SHELL TEMP shares this one
        // flag. Read by wireColorDullDelegate below - VV's OWN native "dull"
        // mechanism (VesselViewer.GetPartColor halves the wire color's RGB
        // when the dull delegate returns true) already IS the "slightly
        // darker tint of the fill" this gives, so no custom darkening math
        // here either. Reachable from the SAME menu already used to pick
        // "SHELL TEMP", no new button/softkey wiring: VV's menu text overlay
        // and its live 3D render are independent channels of the same
        // screen, not alternating states - the toggle item added to
        // CreateMenu below sits on screen right on top of the live rotating
        // vessel.
        private static bool wireframeEnabled = false;

        void Start()
        {
            // Same reasoning as VVEFISAddon: the CLR only resolves a
            // method's type references right before it's JIT-compiled, so
            // keeping every VesselView-touching call out of this method
            // means a missing VesselView install never throws a
            // TypeLoadException here - it just silently does nothing.
            if (!IsVesselViewPresent()) return;
            Register();
        }

        private static bool IsVesselViewPresent()
        {
            // Same CLR-assembly-name check as VVEFISAddon, same reason:
            // AssemblyLoader.LoadedAssembly.name reflects the KSPAssembly
            // attribute, which VesselView Continued stamps identically
            // across all five of its DLLs - see ModPresence.cs's own header.
            return ModPresence.IsLoaded("VesselViewRPM");
        }

        private static void Register()
        {
            VViewCustomMenusMenu.registerMenu(CreateMenu);
            VesselView.VesselViewPlugin.registerCustomMode(BuildSettings());
        }

        private static CustomModeSettings BuildSettings()
        {
            CustomModeSettings settings = new CustomModeSettings
            {
                name = "SHELL TEMP",
                ColorModeOverride = (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION,
                OrientationOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                CenteringOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                MinimodesOverride = (int)CustomModeSettings.OVERRIDE_TYPES.STATIC
            };

            // Same reasoning as VVEFIS: engine icons draw their own
            // hardcoded colors (e.g. red for NOFUEL) as thin lines
            // regardless of ColorModeOverride - left on, they'd sit
            // uncontrolled on top of our own heatmap fill.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVThermalMapColor.GetColor(part);
            settings.wireColorDelegate = (mode, part) => VVThermalMapColor.GetColor(part);

            // Still no alarm/border SEMANTICS here - unlike VVEFIS, this
            // box color carries no Tier/state information, it's purely a
            // static outline for legibility (user request after the first
            // in-game test, 2026-08-30 log 5). Solid opaque black, same
            // static choice VesselView's own VVDiscoDisplay example makes
            // for its box. VesselViewer.GetBoxColor calls this delegate
            // unconditionally once ColorModeOverride is FUNCTION, so it
            // can't be left null regardless of what it returns.
            settings.boxColorDelegate = (mode, part) => Color.black;

            settings.fillColorDullDelegate = mode => false;
            // Off by default: wire == fill exactly, same "fused, invisible"
            // baseline as before this feature existed. On: VV halves the
            // wire color's RGB per part, giving a darker-tinted edge in the
            // part's own heatmap hue instead of a flat, unrelated gray.
            settings.wireColorDullDelegate = mode => wireframeEnabled;
            settings.boxColorDullDelegate = mode => false;

            return settings;
        }

        private static IVViewMenu CreateMenu()
        {
            CustomModeSettings settings = BuildSettings();
            // "MODE ACTIVE" is inert (click target null = stay put) - kept
            // for the same safety reason as before (VViewSimpleMenu.up()/
            // down() on an EMPTY item array drives activeItemPos to -1, and
            // a subsequent click() indexes menuItems[-1] -
            // IndexOutOfRangeException). "WIREFRAME" is the real toggle:
            // VViewSimpleCustomMenuItem's own bool getter/setter constructor -
            // VV renders its own "On"/"Off" suffix via ToString().
            IVVSimpleMenuItem[] items =
            {
                new VViewSimpleCustomMenuItem("MODE ACTIVE"),
                // Trailing space: VViewSimpleCustomMenuItem.ToString() appends
                // the "On"/"Off" suffix directly after the label with no
                // separator of its own - confirmed on VVEFIS ("WIREFRAME" +
                // "On" == "WIREFRAMEOn" without it, log 76).
                new VViewSimpleCustomMenuItem("WIREFRAME ", () => wireframeEnabled, v => wireframeEnabled = v)
            };
            VViewSimpleMenu menu = new VViewSimpleMenu(items, settings.name);
            menu.setCustomSettings(settings);
            return menu;
        }
    }
}
