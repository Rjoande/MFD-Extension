using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVThermalMap
{
    // Registers a new VesselView custom color mode ("SHELL TEMP") through the
    // same public extension points VVEFISAddon uses: purely additive, nothing
    // of VesselView is patched or replaced.
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVThermalMapAddon : MonoBehaviour
    {
        // In-flight wireframe toggle, global to the process rather than
        // per-screen. VV's own "dull" mechanism already halves the wire
        // color's RGB, which is the darker tint of the fill wanted here.
        private static bool wireframeEnabled = false;

        void Start()
        {
            // Same reason as VVEFISAddon: the CLR resolves a method's type
            // references only when JIT-compiling it, so keeping every
            // VesselView call out of here turns a missing install into a
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
                name = "SHELL TEMP",
                ColorModeOverride = (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION,
                OrientationOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                CenteringOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                MinimodesOverride = (int)CustomModeSettings.OVERRIDE_TYPES.STATIC
            };

            // Suppressed: engine icons draw hardcoded colors regardless of
            // ColorModeOverride and would sit on top of the heatmap fill.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVThermalMapColor.GetColor(part);
            settings.wireColorDelegate = (mode, part) => VVThermalMapColor.GetColor(part);

            // No alarm semantics here, unlike VVEFIS: a static outline for
            // legibility only. VesselViewer calls this delegate
            // unconditionally once ColorModeOverride is FUNCTION, so it can
            // never be left null whatever it returns.
            settings.boxColorDelegate = (mode, part) => Color.black;

            settings.fillColorDullDelegate = mode => false;
            // Off: wire == fill exactly, fused and invisible. On: VV halves the
            // wire RGB, an edge in the part's own heatmap hue.
            settings.wireColorDullDelegate = mode => wireframeEnabled;
            settings.boxColorDullDelegate = mode => false;

            return settings;
        }

        private static IVViewMenu CreateMenu()
        {
            CustomModeSettings settings = BuildSettings();
            // "MODE ACTIVE" is inert but required: up()/down() on an EMPTY item
            // array drives activeItemPos to -1 and the next click() then
            // indexes menuItems[-1]. "WIREFRAME" is the real toggle, rendered
            // with VV's own "On"/"Off" suffix.
            IVVSimpleMenuItem[] items =
            {
                new VViewSimpleCustomMenuItem("MODE ACTIVE"),
                // Trailing space on purpose: ToString() appends that suffix
                // straight after the label, with no separator of its own.
                new VViewSimpleCustomMenuItem("WIREFRAME ", () => wireframeEnabled, v => wireframeEnabled = v)
            };
            VViewSimpleMenu menu = new VViewSimpleMenu(items, settings.name);
            menu.setCustomSettings(settings);
            return menu;
        }
    }
}
