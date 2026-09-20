using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVEFIS
{
    // Registers a new VesselView custom color mode ("EFIS SEVERITY") through
    // VesselView's own public extension points, the same ones its bundled
    // VVDiscoDisplay example uses. Purely additive: nothing of VV is patched
    // or replaced, the mode simply joins its "Custom display modes" menu.
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVEFISAddon : MonoBehaviour
    {
        // In-flight wireframe toggle, global to the process rather than
        // per-screen. VV's own "dull" mechanism already halves the wire
        // color's RGB, which is exactly the darker tint of the fill wanted
        // here - no darkening math of our own, just this flag.
        private static bool wireframeEnabled = false;

        void Start()
        {
            // This method must never touch a VesselView type: the CLR resolves
            // a method's type references only when JIT-compiling it, so keeping
            // them out of here turns a missing install into a silent no-op
            // rather than a TypeLoadException.
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
            // The RPM/MAS bridge menu, plus the standalone VV plugin window.
            // On a trimmed install with VesselViewRPM but no VesselViewPlugin
            // this throws inside Start(): Unity logs it and the mode stays off.
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

            // Suppressed: what they show is folded into our own fill, and
            // their colors are hardcoded literals, independent of
            // ColorModeOverride, so they would sit uncontrolled on top of it.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVEFISSeverity.FillColor(part);
            settings.wireColorDelegate = (mode, part) => VVEFISSeverity.FillColor(part);
            settings.boxColorDelegate = (mode, part) => VVEFISSeverity.BoxColor(part);

            settings.fillColorDullDelegate = mode => false;
            // Off: wire == fill exactly, fused and invisible. On: VV halves the
            // wire RGB, an edge in the part's own severity hue rather than a
            // flat unrelated gray.
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
