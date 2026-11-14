// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (Common/src/System/IO/Compression/ZipArchiveEntryConstants.Unix.cs). NetWasm uses the Unix
// external attributes, as upstream's browser and WASI builds do; it has neither Interop.Sys.FileTypes
// nor UnixFileMode, so the values are written out: S_IFREG 0x8000 with mode 0644, and S_IFDIR 0x4000
// with mode 0755.

namespace System.IO.Compression
{
    internal static partial class ZipArchiveEntryConstants
    {
        internal const uint DefaultFileExternalAttributes =
            (0x8000u | // S_IFREG
                (0x100u | 0x80u // UserRead | UserWrite
                    | 0x20u // GroupRead
                    | 0x4u) // OtherRead
            ) << 16;

        internal const uint DefaultDirectoryExternalAttributes =
            (0x4000u | // S_IFDIR
                (0x100u | 0x80u | 0x40u // UserRead | UserWrite | UserExecute
                    | 0x20u | 0x8u // GroupRead | GroupExecute
                    | 0x4u | 0x1u) // OtherRead | OtherExecute
            ) << 16;
    }
}
