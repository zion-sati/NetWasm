// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). The retained helpers implement
// the deterministic Unix lexical path contract; ambient filesystem and current
// directory operations are intentionally not included in NetWasm.

using System.Text;

namespace System.IO;

internal static class PathInternal
{
    internal const char DirectorySeparatorChar = '/';
    internal const char AltDirectorySeparatorChar = '/';
    internal const char VolumeSeparatorChar = '/';
    internal const char PathSeparator = ':';
    internal const string DirectorySeparatorCharAsString = "/";

    internal static int GetRootLength(ReadOnlySpan<char> path) =>
        path.Length > 0 && IsDirectorySeparator(path[0]) ? 1 : 0;

    internal static bool IsDirectorySeparator(char character) => character == DirectorySeparatorChar;

    internal static bool StartsWithDirectorySeparator(ReadOnlySpan<char> path) =>
        path.Length > 0 && IsDirectorySeparator(path[0]);

    internal static bool IsRoot(ReadOnlySpan<char> path) => path.Length == GetRootLength(path);

    internal static bool IsPartiallyQualified(ReadOnlySpan<char> path) => !Path.IsPathRooted(path);

    internal static bool IsEffectivelyEmpty(string? path) => string.IsNullOrEmpty(path);

    internal static bool IsEffectivelyEmpty(ReadOnlySpan<char> path) => path.IsEmpty;

    internal static bool EndsInDirectorySeparator([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? path) =>
        !string.IsNullOrEmpty(path) && IsDirectorySeparator(path![^1]);

    internal static bool EndsInDirectorySeparator(ReadOnlySpan<char> path) =>
        path.Length > 0 && IsDirectorySeparator(path[^1]);

    internal static string? TrimEndingDirectorySeparator(
        [System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(path))] string? path) =>
        EndsInDirectorySeparator(path) && !IsRoot(path!.AsSpan()) ? path.Substring(0, path.Length - 1) : path;

    internal static ReadOnlySpan<char> TrimEndingDirectorySeparator(ReadOnlySpan<char> path) =>
        EndsInDirectorySeparator(path) && !IsRoot(path) ? path.Slice(0, path.Length - 1) : path;

    internal static string? NormalizeDirectorySeparators(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var normalized = true;
        for (var index = 1; index < path!.Length; index++)
        {
            if (IsDirectorySeparator(path[index]) && IsDirectorySeparator(path[index - 1]))
            {
                normalized = false;
                break;
            }
        }

        if (normalized)
        {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        for (var index = 0; index < path.Length; index++)
        {
            if (IsDirectorySeparator(path[index]) && index > 0 && IsDirectorySeparator(path[index - 1]))
            {
                continue;
            }

            builder.Append(path[index]);
        }

        return builder.ToString();
    }

    internal static bool AreRootsEqual(string first, string second) =>
        GetRootLength(first.AsSpan()) == GetRootLength(second.AsSpan());

    internal static int GetCommonPathLength(string first, string second)
    {
        var commonLength = 0;
        var maximum = Math.Min(first.Length, second.Length);
        while (commonLength < maximum && first[commonLength] == second[commonLength])
        {
            commonLength++;
        }

        if (commonLength == 0)
        {
            return 0;
        }

        if (commonLength == first.Length &&
            (commonLength == second.Length || IsDirectorySeparator(second[commonLength])))
        {
            return commonLength;
        }

        if (commonLength == second.Length && IsDirectorySeparator(first[commonLength]))
        {
            return commonLength;
        }

        // Do not treat a shared prefix in the middle of a segment as a common path.
        while (commonLength > 0 && !IsDirectorySeparator(first[commonLength - 1]))
        {
            commonLength--;
        }

        return commonLength;
    }
}
