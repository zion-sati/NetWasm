// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.IO.FileSystem.Watcher uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.IO.FileSystem.Watcher's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string FileSystemWatcher_PlatformNotSupported = "System.IO.FileSystem.Watcher is not supported on this platform.";
        internal const string FSW_BufferOverflow = "Too many changes at once in directory:{0}.";
        internal const string FSW_WatchedDirectoryDeletedOrMoved = "The directory being watched '{0}' was deleted or moved.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string InvalidDirName = "The directory name {0} is invalid.";
        internal const string InvalidDirName_NotExists = "The directory name '{0}' does not exist.";
        internal const string InvalidEnumArgument = "The value of argument '{0}' ({1}) is invalid for Enum type '{2}'.";
    }
}
