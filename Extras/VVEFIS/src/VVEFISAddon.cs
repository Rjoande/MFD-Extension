using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVEFIS
{
    // Registers a new VesselView custom color mode ("EFIS SEVERITY") using
    // VesselView's own public extension points (CustomModeSettings +
    // VViewCustomMenusMenu.registerMenu / VesselViewPlugin.registerCustomMode) -
    // the same mechanism VV's own bundled VVDiscoDisplay example plugin uses.
    // Purely additive: installs alongside VesselView/VesselViewRPM without
    // patching or replacing any of its files, reachable from its own "Custom
    // display modes" menu. See MFDExtension/CLAUDE.md and HOSTING.md for the
    // full design history (this satellite lives under MFDExtension/Extras/
    // for now, promotable to its own mod later).
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVEFISAddon : MonoBehaviour
    {
        // In-flight wireframe toggle (2026-09-01, user's request, after the
        // separate "EFIS WF" satellite was tried and archived - see
        // _deprecated/VVEFISWF/). Global to the process, not per-screen (the
        // user's explicit call): every VV monitor running EFIS SEVERITY
        // shares this one flag. Read by wireColorDullDelegate below - VV's
        // OWN native "dull" mechanism (VesselViewer.GetPartColor halves the
        // wire color's RGB when the dull delegate returns true) already IS
        // the "slightly darker tint of the fill" the user asked about, so no
        // custom darkening math was needed, just wiring this flag into it.
        // Reachable from the SAME menu already used to pick "EFIS SEVERITY"
        // in the first place, no new button/softkey wiring: VV's menu text
        // overlay and its live 3D render are independent channels of the
        // same screen (InternalVesselView.ShowMenu vs. RenderViewer), not
        // alternating states - the toggle item added to CreateMenu below sits
        // on screen right on top of the live rotating vessel.
        private static bool wireframeEnabled = false;

        void Start()
        {
            // This method must never touch a VesselView/VesselViewRPM type
            // directly - the CLR only resolves a method's type references when
            // that method is actually JIT-compiled, right before its first
            // call. Keeping the presence check here (pure stock-KSP API, no
            // VesselView reference) and all VesselView-touching code in a
            // separate method means a missing VesselView install never throws
            // a TypeLoadException here, it just silently does nothing.
            if (!IsVesselViewPresent()) return;
            Register();
        }

        private static bool IsVesselViewPresent()
        {
            // Routed through the shared ModPresence.IsLoaded (2026-08-24
            // refactor) - it compares the CLR assembly name, NOT
            // LoadedAssembly.name: the latter holds the KSPAssembly
            // attribute name when one is declared, and the VesselView
            // Continued fork stamps EVERY one of its five DLLs with the
            // same KSPAssembly name ("VesselViewerContinued") - confirmed
            // on the real install's Player.log (2026-08-22 test: our mode
            // never registered, no error, because a same-file check
            // against LoadedAssembly.name silently never matched). The CLR
            // name stays "VesselViewRPM" and is also what our compile-time
            // references actually bind to, so it's the right thing to test.
            return ModPresence.IsLoaded("VesselViewRPM");
        }

        private static void Register()
        {
            // Registers with the RPM/MAS bridge (reachable via a monitor's own
            // "Custom display modes" menu) and, for completeness, with the
            // standalone VesselView plugin window too - same pattern as VV's
            // own VVDiscoDisplay example. If VesselViewRPM is present but
            // VesselViewPlugin.dll happens to be missing (an unusual, manually
            // trimmed install), this throws inside Start() - Unity logs it and
            // moves on, it doesn't crash the game, just leaves this feature off.
            VViewCustomMenusMenu.registerMenu(CreateMenu);
            VesselView.VesselViewPlugin.registerCustomMode(BuildSettings());
        }

        private static CustomModeSettings BuildSettings()
        {
            CustomModeSettings settings = new CustomModeSettings
            {
                name = "EFIS SEVERITY",
                ColorModeOverride = (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION,
                OrientationOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                CenteringOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                MinimodesOverride = (int)CustomModeSettings.OVERRIDE_TYPES.STATIC
            };

            // Suppress the native engine icons entirely - their information
            // (no fuel / no power / no air / inactive) is folded into our own
            // fill color instead. Their hardcoded colors (e.g. Color.red for
            // NOFUEL, drawn as thin lines, independent of ColorModeOverride)
            // would otherwise sit uncontrolled on top of our own fill and hurt
            // legibility - see CLAUDE.md design log for the full reasoning.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVEFISSeverity.FillColor(part);
            settings.wireColorDelegate = (mode, part) => VVEFISSeverity.FillColor(part);
            settings.boxColorDelegate = (mode, part) => VVEFISSeverity.BoxColor(part);

            settings.fillColorDullDelegate = mode => false;
            // Off by default: wire == fill exactly, same "fused, invisible"
            // baseline as before this feature existed. On: VV halves the
            // wire color's RGB per part, giving a darker-tinted edge in the
            // part's own severity hue instead of a flat, unrelated gray.
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
            // a subsequent click() indexes menuItems[-1] - an
            // IndexOutOfRangeException). "WIREFRAME" is the real toggle
            // (2026-09-01): VViewSimpleCustomMenuItem's own bool getter/
            // setter constructor - VV renders its own "ON"/"OFF" suffix via
            // ToString(), nothing to format here.
            IVVSimpleMenuItem[] items =
            {
                new VViewSimpleCustomMenuItem("MODE ACTIVE"),
                // Trailing space: VViewSimpleCustomMenuItem.ToString()
                // appends ViewerConstants.boolAsString's "On"/"Off" directly
                // after the label with no separator of its own (verified on
                // the decompiled source - "WIREFRAME" + "On" == "WIREFRAMEOn"
                // otherwise), confirmed in-game by the user.
                new VViewSimpleCustomMenuItem("WIREFRAME ", () => wireframeEnabled, v => wireframeEnabled = v)
            };
            VViewSimpleMenu menu = new VViewSimpleMenu(items, settings.name);
            menu.setCustomSettings(settings);
            return menu;
        }
    }
}
