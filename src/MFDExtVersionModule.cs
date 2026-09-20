using System.Reflection;

namespace MFDExtension
{
    // MAS companion module for the hub's version line, registered the same
    // manual-companion way as MFDExtCasModule (MAS_BasicMFD.cfg section 6).
    // Reads AssemblyVersion at runtime, so the on-screen text cannot drift
    // from the csproj's <Version> the way a static TEXT literal did.
    public class MFDExtVersionModule : InternalModule
    {
        private static string versionTag;

        private static string VersionTag
        {
            get
            {
                if (versionTag == null)
                {
                    System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    versionTag = "v" + v.Major + "." + v.Minor + "." + v.Build;
                }
                return versionTag;
            }
        }

        public string GetVersionLine(int screenWidth, int screenHeight)
        {
            return "MFD Extended " + VersionTag;
        }

        public string GetStatusLine(int screenWidth, int screenHeight)
        {
            return "-[hw] MFDEXT[/hw] MFD Extended " + VersionTag;
        }
    }
}
