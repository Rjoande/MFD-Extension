namespace MFDExtension.Shared
{
    // The severity scale shared by every channel reporting vessel health:
    // VVEFIS maps a tier to a fill color, CAS to a text section header.
    // Ordinal order IS the severity order - callers compare with < / >.
    internal enum Tier
    {
        Indication,
        Advisory,
        Caution,
        Warning
    }
}
