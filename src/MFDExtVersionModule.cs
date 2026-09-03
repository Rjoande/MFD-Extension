using System.Reflection;

namespace MFDExtension
{
    // MAS companion module for the hub's version line (Pages/MFDExt_Stby.cfg),
    // registered the same manual-companion-MODULE way as MFDExtCasModule
    // (src/Cas/MFDExtCasModule.cs) - see Config/Additive/MAS_BasicMFD.cfg §6.
    // Reads AssemblyVersion at runtime so the on-screen "MFD Extended vX.Y.Z"
    // text can never drift from MFDExtension.csproj's <Version> the way the
    // old static TEXT literal did (stuck at v0.1.0 through the v0.1.1 and
    // v0.2.0 releases - see CLAUDE.md).
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
