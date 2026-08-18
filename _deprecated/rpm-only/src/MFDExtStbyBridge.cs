using System;
using System.Reflection;
using UnityEngine;

namespace MFDExtension
{
    // Bridges a host RPM monitor's globalButton click (e.g. buttonR9 on
    // BasicMFD, otherwise unreachable for page-switching - RPM routes
    // globalButtons to GlobalButtonClick unconditionally, never to
    // PageButtonClick, see GameData/MFDExtension/CLAUDE.md 2026-08-09)
    // into a jump to our own additive page, via reflection into
    // RasterPropMonitor's private FindPageByName + public PageButtonClick.
    //
    // Deliberately defensive: every reflection step is null-checked, and any
    // exception is caught and logged rather than thrown. If RPM's private
    // internals ever change shape, this silently does nothing - the
    // button_STBY cycle (added by the same host patch) remains the reliable
    // fallback path regardless of whether this shortcut still works.
    public class MFDExtStbyBridge : InternalModule
    {
        [KSPField]
        public string targetPageName = "stby_MFDExt";

        // Index within the host's own globalButtons list - NOT universal,
        // must be set per host patch (e.g. 7 for buttonR9 on BasicMFD's
        // order: UP,DOWN,ENTER,ESC,HOME,RIGHT,LEFT,R9,R10 = 0..8).
        [KSPField]
        public int triggerButtonIndex = 7;

        // Required by BACKGROUNDHANDLER's `method` field signature. We draw
        // nothing - this handler exists only for buttonClickMethod below -
        // `false` tells RPM no new background content was produced.
        public bool RenderNothing(RenderTexture screen, float cameraAspect)
        {
            return false;
        }

        public void HandleButtonClick(int buttonNumber)
        {
            if (buttonNumber != triggerButtonIndex)
            {
                return;
            }

            try
            {
                object rpm = FindSiblingRasterPropMonitor();
                if (rpm == null)
                {
                    return;
                }

                Type rpmType = rpm.GetType();
                MethodInfo findPage = rpmType.GetMethod("FindPageByName",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo pageButtonClick = rpmType.GetMethod("PageButtonClick",
                    BindingFlags.Public | BindingFlags.Instance);

                if (findPage == null || pageButtonClick == null)
                {
                    Debug.LogWarning("[MFDExtension] RPM reflection targets missing (FindPageByName/PageButtonClick) - falling back to STBY cycle only.");
                    return;
                }

                object targetPage = findPage.Invoke(rpm, new object[] { targetPageName });
                if (targetPage == null)
                {
                    Debug.LogWarning("[MFDExtension] RPM page '" + targetPageName + "' not found by FindPageByName - falling back to STBY cycle only.");
                    return;
                }

                pageButtonClick.Invoke(rpm, new object[] { targetPage });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MFDExtension] R9 reflection jump failed, falling back to STBY cycle: " + ex.Message);
            }
        }

        private object FindSiblingRasterPropMonitor()
        {
            if (internalProp == null)
            {
                return null;
            }

            foreach (InternalModule module in internalProp.internalModules)
            {
                if (module != null && module.GetType().Name == "RasterPropMonitor")
                {
                    return module;
                }
            }

            return null;
        }
    }
}
