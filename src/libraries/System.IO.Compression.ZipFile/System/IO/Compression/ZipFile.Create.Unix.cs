// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: a library cannot reach CoreLib's Interop.Sys, so the entry type comes from public APIs. A
// directory or a link to one is a directory; anything else that resolves is a file. The public API has no file
// type, so a FIFO, socket or device in the tree is archived as a file, where upstream skips it as unsupported.

using System.IO.Enumeration;

namespace System.IO.Compression
{
    public static partial class ZipFile
    {
        private static FileSystemEnumerable<(string, CreateEntryType)> CreateEnumerableForCreate(string directoryFullPath)
            => new FileSystemEnumerable<(string, CreateEntryType)>(directoryFullPath,
                static (ref FileSystemEntry entry) =>
                    {
                        string fullPath = entry.ToFullPath();
                        if (entry.IsDirectory) // entry is a directory, or a link to a directory.
                        {
                            return (fullPath, CreateEntryType.Directory);
                        }

                        // As upstream's stat, follow links: a broken one fails.
                        if (!File.Exists(fullPath))
                        {
                            throw new FileNotFoundException(SR.Format(SR.IO_FileNotFound_FileName, fullPath), fullPath);
                        }
                        return (fullPath, CreateEntryType.File);
                    },
                    new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false });
    }
}
