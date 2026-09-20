namespace MFDExtension.Shared
{
    // "Is mod X installed?" for the whole project. Compares the CLR assembly
    // name, NEVER AssemblyLoader.LoadedAssembly.name: that one holds the
    // KSPAssembly attribute, which a mod may stamp on several of its DLLs
    // alike (all five VesselView Continued DLLs share one).
    internal static class ModPresence
    {
        // Several candidates because forks rename the assembly ("DangIt" vs
        // "DangItContinued").
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
