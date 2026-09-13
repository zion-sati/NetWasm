// Adapted from dotnet/runtime System.Configuration.Assemblies metadata.
// The upstream implementation is licensed under MIT.

namespace System.Configuration.Assemblies
{
    public enum AssemblyHashAlgorithm
    {
        None = 0,
        MD5 = 0x8003,
        SHA1 = 0x8004,
        SHA256 = 0x800c,
        SHA384 = 0x800d,
        SHA512 = 0x800e
    }

    public enum AssemblyVersionCompatibility
    {
        SameMachine = 1,
        SameProcess = 2,
        SameDomain = 3
    }
}
