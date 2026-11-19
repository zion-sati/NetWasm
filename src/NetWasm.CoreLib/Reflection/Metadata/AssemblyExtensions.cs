// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Reflection.Metadata
{
    public static class AssemblyExtensions
    {
        // As Native AOT's: a compiled program carries no ECMA-335 metadata blob for System.Reflection.Metadata to read.
        [CLSCompliant(false)] // out byte* blob
        public static unsafe bool TryGetRawMetadata(this Assembly assembly, out byte* blob, out int length)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            blob = null;
            length = 0;
            return false;
        }
    }
}
