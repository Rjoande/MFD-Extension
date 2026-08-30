namespace MFDExtension.Shared
{
    // THE single "is mod X installed?" check for this project (2026-08-24
    // "shared basket" refactor). Compares the CLR assembly name
    // (assembly.GetName().Name), NEVER AssemblyLoader.LoadedAssembly.name:
    // the latter holds the KSPAssembly attribute's declared name when one
    // is present, which can be shared across multiple physically different
    // DLLs - the exact bug that made VVEFIS invisible on its first in-game
    // test (all five VesselView Continued DLLs declare KSPAssembly
    // "VesselViewerContinued", see CLAUDE.md log 41). The CLR name is
    // per-DLL and is also what compile-time references bind to.
    //
    // Accepts multiple candidate names because the same mod ships under
    // different assembly names across forks/eras: DangIt's classic DLL is
    // "DangIt", the linuxgurugamer Continued fork's is "DangItContinued"
    // (the user's real install carries the latter, confirmed 2026-08-24;
    // this workspace's copy declares no KSPAssembly attribute at all,
    // verified on the DLL's raw metadata).
    internal static class ModPresence
    {
        internal static bool IsLoaded(params string[] clrAssemblyNames)
        {
            foreach (AssemblyLoader.LoadedAssembly loaded in AssemblyLoader.loadedAssemblies)
            {
                string name = loaded.assembly.GetName().Name;
                foreach (string candidate in clrAssemblyNames)
                {
                    if (name == candidate) return true;
                }
            }
            return false;
        }
    }
}
