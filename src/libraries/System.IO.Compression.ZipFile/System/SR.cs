// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.IO.Compression.ZipFile uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.IO.Compression.ZipFile's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string EmptyPassword = "The password cannot be empty.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string ZipUnsupportedFile = "The file type of '{0}' is not supported for zip archiving.";
        internal const string IO_DirectoryNotAllowed = "The specified path '{0}' is a directory, which is not allowed in this operation.";
        internal const string UnwritableStream = "The stream is unwritable.";
        internal const string UnreadableStream = "The stream is unreadable.";
        internal const string IO_DirectoryNameWithData = "Zip entry name ends in directory separator character but contains data.";
        internal const string IO_ExtractingResultsInOutside = "Extracting Zip entry would have resulted in a file outside the specified destination directory.";
        internal const string IO_FileNotFound_FileName = "Could not find file '{0}'.";
    }
}
