using MFDExtension.Shared;
using UnityEngine;
using VesselView;
using VesselViewRPM.menus;

namespace VVEFISWF
{
    // Registers a second VesselView custom color mode, "EFIS WF" - the exact
    // same severity palette as VVEFIS's "EFIS SEVERITY" (VVEFISSeverity.cs,
    // shared via src/Shared/, see that file's header), but with a visible
    // wireframe overlay traced over the solid fill instead of none - the
    // same "3D cage" look VesselView's own native color modes get for free
    // (VesselViewer.renderPart draws every mesh TWICE per part, once solid
    // via fillColorDelegate/GL.wireframe=false, once as pure wireframe via
    // wireColorDelegate/GL.wireframe=true - VVEFIS already opts into that
    // second pass, it just currently reuses the fill color, which makes it
    // blend in and vanish).
    //
    // A DELIBERATELY SEPARATE satellite (2026-09-01, user's explicit
    // request), not a second mode registered from inside VVEFIS.dll: own
    // folder (Extras/VVEFISWF/, sibling to Extras/VVEFIS/), own .csproj, own
    // compiled DLL. The severity computation itself is source-linked from
    // src/Shared/VVEFISSeverity.cs (compiled INTO this DLL, no runtime
    // reference to VVEFIS.dll) so the two twins can never disagree on what
    // color a part should be - only this file's wire color and the mode name
    // differ. Deleting this Extras/VVEFISWF/ folder removes "EFIS WF" and
    // leaves "EFIS SEVERITY" (VVEFIS) completely unaffected, and vice versa -
    // that mutual independence was the whole point of splitting it out.
    [KSPAddon(KSPAddon.Startup.Flight, true)]
    public class VVEFISWFAddon : MonoBehaviour
    {
        // Light attenuated gray, not pure white - user's explicit call
        // (2026-09-01) after seeing how "heavy"/cage-like VV's native pure-
        // white wireframe reads on a real part mesh (every triangle edge of
        // the actual model, not a simplified box - GL.wireframe has no
        // exposed line-width control in Unity, and the shared lineMaterial
        // is Shader.Find("Unlit/Color") - opaque, no alpha blending, so
        // "thinner" or "more transparent" aren't real options here, only the
        // RGB value itself is - both verified on VesselViewer.cs before
        // proposing this as the only real lever). wireColorDullDelegate
        // stays false below so this value is used as-is, not halved again.
        private static readonly Color WireframeGray = new Color(0.55f, 0.55f, 0.55f);

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
            // Same check as VVEFISAddon - see that file's comment for why it
            // compares the CLR assembly name, not LoadedAssembly.name.
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
                name = "EFIS WF",
                ColorModeOverride = (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION,
                OrientationOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                CenteringOverride = (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC,
                MinimodesOverride = (int)CustomModeSettings.OVERRIDE_TYPES.STATIC
            };

            // Same reasoning as VVEFIS: the native engine icons' hardcoded
            // colors would sit uncontrolled on top of our own fill.
            settings.staticSettings.displayEngines = false;

            settings.fillColorDelegate = (mode, part) => VVEFISSeverity.FillColor(part);
            // The one real difference from VVEFIS's "EFIS SEVERITY": a
            // dedicated attenuated gray instead of reusing FillColor, so the
            // wireframe pass is actually visible as a wireframe instead of
            // blending into the fill.
            settings.wireColorDelegate = (mode, part) => WireframeGray;
            settings.boxColorDelegate = (mode, part) => VVEFISSeverity.BoxColor(part);

            settings.fillColorDullDelegate = mode => false;
            settings.wireColorDullDelegate = mode => false;
            settings.boxColorDullDelegate = mode => false;

            return settings;
        }

        private static IVViewMenu CreateMenu()
        {
            CustomModeSettings settings = BuildSettings();
            // See VVEFISAddon's identical comment: a menu with zero items
            // crashes on the first up/down/enter press (VViewSimpleMenu on an
            // empty array). One inert item keeps every button safe.
            IVVSimpleMenuItem[] items = { new VViewSimpleCustomMenuItem("MODE ACTIVE") };
            VViewSimpleMenu menu = new VViewSimpleMenu(items, settings.name);
            menu.setCustomSettings(settings);
            return menu;
        }
    }
}
