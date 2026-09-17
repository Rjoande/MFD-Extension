using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVHolo
{
    // Registers a new VesselView custom color mode ("HOLO") using the same
    // public extension points VVEFISAddon/VVThermalMapAddon use
    // (CustomModeSettings + VViewCustomMenusMenu.registerMenu /
    // VesselViewPlugin.registerCustomMode) - purely additive, installs
    // alongside VesselView/VesselViewRPM without patching or replacing any
    // of its files. See MFDExtension/CLAUDE.md for the full design history.
    //
    // No wireframe toggle here (unlike VVEFIS/VVThermalMap): those modes
    // default wire == fill (fused, invisible) and offer an opt-in toggle to
    // darken the wire into a visible edge. HOLO's wireframe is a distinct
    // color from the fill from the start (light blue vs cobalt) - it's
    // always visible, there's nothing to toggle.
    //
    // No engine-icon recoloring either (surfaced as a real obstacle before
    // implementation): VesselViewer.renderEngineThrusts passes each icon's
    // color as a hardcoded literal (Color.red, Color.cyan, ...) straight
    // into renderIcon, with no delegate in between - unlike fill/wire/box,
    // there's no public hook to override it. Recoloring them to a coherent
    // "Cherenkov blue" would need a Harmony patch on a private VV method -
    // the project's first, and a real patch (even if IL-level, not a file
    // on disk) on code that has otherwise never been touched outside its
    // own public extension points. User's call: suppress them instead
    // (staticSettings.displayEngines = false), same choice VVEFIS/
    // VVThermalMap already make for the same underlying reason (their own
    // hardcoded colors would sit uncontrolled on top of this mode's fill).
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVHoloAddon : MonoBehaviour
    {
        void Start()
        {
            // Same reasoning as VVEFISAddon/VVThermalMapAddon: the CLR only
            // resolves a method's type references right before it's
            // JIT-compiled, so keeping every VesselView-touching call out
            // of this method means a missing VesselView install never
            // throws a TypeLoadException here - it just silently does
            // nothing.
            if (!IsVesselViewPresent()) return;
            Register();
        }

        private static bool IsVesselViewPresent()
        {
            // Same CLR-assembly-name check as VVEFISAddon/VVThermalMapAddon,
            // same reason: AssemblyLoader.LoadedAssembly.name reflects the
            // KSPAssembly attribute, which VesselView Continued stamps
            // identically across all five of its DLLs - see ModPresence.cs.
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
                name = "HOLO",
                ColorModeOverride = (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION,
                OrientationOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                CenteringOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                MinimodesOverride = (int)CustomModeSettings.OVERRIDE_TYPES.STATIC
            };

            // Suppressed on purpose - see the class header above.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVHoloPalette.FillColor(part);
            settings.wireColorDelegate = (mode, part) => VVHoloPalette.WireColor(part);
            settings.boxColorDelegate = (mode, part) => VVHoloPalette.BoxColor(part);

            // VV calls all three *DullDelegate unconditionally once
            // ColorModeOverride is FUNCTION (verified on the real source,
            // VesselViewer.GetPartColor/GetBoxColor - no null check), so
            // they can't be left unset even though this mode never uses
            // the native "dull" mechanism (WireColor already returns the
            // correct flat dark red for WARNING parts directly).
            settings.fillColorDullDelegate = mode => false;
            settings.wireColorDullDelegate = mode => false;
            settings.boxColorDullDelegate = mode => false;

            return settings;
        }

        private static IVViewMenu CreateMenu()
        {
            CustomModeSettings settings = BuildSettings();
            // Single inert item ("MODE ACTIVE", click target null = stay
            // put) - required even with nothing to toggle: VViewSimpleMenu.
            // up()/down() on an EMPTY item array drives activeItemPos to -1,
            // and a subsequent click() indexes menuItems[-1] -
            // IndexOutOfRangeException (same guard VVEFIS/VVThermalMap use).
            IVVSimpleMenuItem[] items =
            {
                new VViewSimpleCustomMenuItem("MODE ACTIVE")
            };
            VViewSimpleMenu menu = new VViewSimpleMenu(items, settings.name);
            menu.setCustomSettings(settings);
            return menu;
        }
    }
}
