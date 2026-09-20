using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVHolo
{
    // Registers a new VesselView custom color mode ("HOLO") through the same
    // public extension points the other two satellites use: purely additive,
    // nothing of VesselView is patched or replaced.
    //
    // No wireframe toggle here, unlike VVEFIS/VVThermalMap: their wire matches
    // the fill until the toggle darkens it, whereas HOLO's is a distinct color
    // from the start and always visible - there is nothing to switch.
    //
    // No engine-icon recoloring either: renderEngineThrusts passes hardcoded
    // color literals straight to renderIcon with no delegate in between, so
    // overriding them would need a Harmony patch on a private VV method. They
    // are suppressed instead, as in the other two modes.
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVHoloAddon : MonoBehaviour
    {
        void Start()
        {
            // Same reason as the other two satellites: the CLR resolves a
            // method's type references only when JIT-compiling it, so keeping
            // every VesselView call out of here turns a missing install into a
            // silent no-op rather than a TypeLoadException.
            if (!IsVesselViewPresent()) return;
            Register();
        }

        private static bool IsVesselViewPresent()
        {
            // By CLR assembly name: VesselView Continued stamps one shared
            // KSPAssembly name on all five of its DLLs - see ModPresence.
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
            // ColorModeOverride is FUNCTION, with no null check, so they must
            // be set even though this mode never uses the "dull" mechanism.
            settings.fillColorDullDelegate = mode => false;
            settings.wireColorDullDelegate = mode => false;
            settings.boxColorDullDelegate = mode => false;

            return settings;
        }

        private static IVViewMenu CreateMenu()
        {
            CustomModeSettings settings = BuildSettings();
            // One inert item, required even with nothing to toggle: up()/down()
            // on an EMPTY item array drives activeItemPos to -1 and the next
            // click() then indexes menuItems[-1].
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
