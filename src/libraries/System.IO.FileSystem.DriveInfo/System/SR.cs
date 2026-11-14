// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.IO.FileSystem.DriveInfo uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.IO.FileSystem.DriveInfo's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string IO_DriveNotFound = "Could not find the drive. The drive might not be ready or might not be mapped.";
        internal const string Arg_InvalidDriveChars = "Illegal characters in drive name '{0}'.";
        internal const string Arg_MustBeNonEmptyDriveName = "Drive name must not be empty.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
    }
}
