// Contract adapted from dotnet/runtime System.Private.CoreLib.
// Licensed under the MIT license; see the upstream repository for the
// complete copyright notice.
namespace System
{
    [Flags]
    public enum StringSplitOptions
    {
        None = 0,
        RemoveEmptyEntries = 1,
        TrimEntries = 2
    }
}
