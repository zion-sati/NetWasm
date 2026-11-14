// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: the file's mode comes from File.GetUnixFileMode; a library cannot reach CoreLib's Interop.Sys.

using System.Diagnostics;

using static System.IO.Compression.ZipArchiveEntryConstants;

namespace System.IO.Compression
{
    public static partial class ZipFileExtensions
    {
        static partial void SetExternalAttributes(FileStream fs, ZipArchiveEntry entry)
        {
            Debug.Assert(!OperatingSystem.IsWindows());

            // SetExternalAttributes is only used to overwrite the default values when reading on the unix systems
            // This assert ensures that nothing else sets values different to the default before
            // overwriting it with the data read from the files
            Debug.Assert((uint)entry.ExternalAttributes == DefaultFileExternalAttributes);

            // NetWasm: the stream is a regular file, and File.GetUnixFileMode reads the mode upstream's FStat does.
            entry.ExternalAttributes = (int)((0x8000u /* S_IFREG */ | (uint)File.GetUnixFileMode(fs.SafeFileHandle)) << 16);
        }
    }
}
