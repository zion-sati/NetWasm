// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). Only deterministic lexical path
// operations are retained; full-path, existence, temporary-file, and other
// ambient platform operations are intentionally outside NetWasm.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.IO;

public static class Path
{
    public static readonly char DirectorySeparatorChar = PathInternal.DirectorySeparatorChar;
    public static readonly char AltDirectorySeparatorChar = PathInternal.AltDirectorySeparatorChar;
    public static readonly char VolumeSeparatorChar = PathInternal.VolumeSeparatorChar;
    public static readonly char PathSeparator = PathInternal.PathSeparator;

    public static string? ChangeExtension(string? path, string? extension)
    {
        if (path is null)
        {
            return null;
        }

        var subLength = path.Length;
        if (subLength == 0)
        {
            return string.Empty;
        }

        for (var index = path.Length - 1; index >= 0; index--)
        {
            var character = path[index];
            if (character == '.')
            {
                subLength = index;
                break;
            }

            if (PathInternal.IsDirectorySeparator(character))
            {
                break;
            }
        }

        if (extension is null)
        {
            return path.Substring(0, subLength);
        }

        var subpath = path.AsSpan(0, subLength);
        return extension.StartsWith('.') ? string.Concat(subpath, extension) : string.Concat(subpath, ".", extension);
    }

    public static string? GetDirectoryName(string? path)
    {
        if (path is null || PathInternal.IsEffectivelyEmpty(path.AsSpan()))
        {
            return null;
        }

        var end = GetDirectoryNameOffset(path.AsSpan());
        return end >= 0 ? PathInternal.NormalizeDirectorySeparators(path.Substring(0, end)) : null;
    }

    public static ReadOnlySpan<char> GetDirectoryName(ReadOnlySpan<char> path)
    {
        if (PathInternal.IsEffectivelyEmpty(path))
        {
            return ReadOnlySpan<char>.Empty;
        }

        var end = GetDirectoryNameOffset(path);
        return end >= 0 ? path.Slice(0, end) : ReadOnlySpan<char>.Empty;
    }

    private static int GetDirectoryNameOffset(ReadOnlySpan<char> path)
    {
        var rootLength = PathInternal.GetRootLength(path);
        var end = path.Length;
        if (end <= rootLength)
        {
            return -1;
        }

        while (end > rootLength && !PathInternal.IsDirectorySeparator(path[--end]))
        {
        }

        while (end > rootLength && PathInternal.IsDirectorySeparator(path[end - 1]))
        {
            end--;
        }

        return end;
    }

    public static string? GetExtension(string? path)
    {
        if (path is null)
        {
            return null;
        }

        return GetExtension(path.AsSpan()).ToString();
    }

    public static ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> path)
    {
        for (var index = path.Length - 1; index >= 0; index--)
        {
            var character = path[index];
            if (character == '.')
            {
                return index == path.Length - 1 ? ReadOnlySpan<char>.Empty : path.Slice(index);
            }

            if (PathInternal.IsDirectorySeparator(character))
            {
                break;
            }
        }

        return ReadOnlySpan<char>.Empty;
    }

    public static string? GetFileName(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var result = GetFileName(path.AsSpan());
        return path.Length == result.Length ? path : result.ToString();
    }

    public static ReadOnlySpan<char> GetFileName(ReadOnlySpan<char> path)
    {
        var root = GetPathRoot(path).Length;
        var index = path.LastIndexOf(PathInternal.DirectorySeparatorChar);
        return path.Slice(index < root ? root : index + 1);
    }

    public static string? GetFileNameWithoutExtension(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var result = GetFileNameWithoutExtension(path.AsSpan());
        return path.Length == result.Length ? path : result.ToString();
    }

    public static ReadOnlySpan<char> GetFileNameWithoutExtension(ReadOnlySpan<char> path)
    {
        var fileName = GetFileName(path);
        var lastPeriod = fileName.LastIndexOf('.');
        return lastPeriod < 0 ? fileName : fileName.Slice(0, lastPeriod);
    }

    public static bool IsPathFullyQualified(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsPathFullyQualified(path.AsSpan());
    }

    public static bool IsPathFullyQualified(ReadOnlySpan<char> path) => !PathInternal.IsPartiallyQualified(path);

    public static bool HasExtension([NotNullWhen(true)] string? path) => path is not null && HasExtension(path.AsSpan());

    public static bool HasExtension(ReadOnlySpan<char> path)
    {
        for (var index = path.Length - 1; index >= 0; index--)
        {
            var character = path[index];
            if (character == '.')
            {
                return index != path.Length - 1;
            }

            if (PathInternal.IsDirectorySeparator(character))
            {
                break;
            }
        }

        return false;
    }

    public static string Combine(string path1, string path2)
    {
        ArgumentNullException.ThrowIfNull(path1);
        ArgumentNullException.ThrowIfNull(path2);
        return CombineInternal(path1, path2);
    }

    public static string Combine(string path1, string path2, string path3)
    {
        ArgumentNullException.ThrowIfNull(path1);
        ArgumentNullException.ThrowIfNull(path2);
        ArgumentNullException.ThrowIfNull(path3);
        return CombineInternal(CombineInternal(path1, path2), path3);
    }

    public static string Combine(string path1, string path2, string path3, string path4)
    {
        ArgumentNullException.ThrowIfNull(path1);
        ArgumentNullException.ThrowIfNull(path2);
        ArgumentNullException.ThrowIfNull(path3);
        ArgumentNullException.ThrowIfNull(path4);
        return CombineInternal(CombineInternal(CombineInternal(path1, path2), path3), path4);
    }

    public static string Combine(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Combine((ReadOnlySpan<string>)paths);
    }

    public static string Combine(params ReadOnlySpan<string> paths)
    {
        var result = string.Empty;
        for (var index = 0; index < paths.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(paths[index], nameof(paths));
            if (paths[index].Length != 0)
            {
                result = result.Length == 0 ? paths[index] : CombineInternal(result, paths[index]);
            }
        }

        return result;
    }

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2) =>
        JoinNonEmpty(path1, path2);

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3) =>
        JoinNonEmpty(path1, path2, path3);

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3, ReadOnlySpan<char> path4) =>
        JoinNonEmpty(path1, path2, path3, path4);

    public static string Join(string? path1, string? path2)
    {
        return JoinStrings(path1, path2);
    }

    public static string Join(string? path1, string? path2, string? path3)
    {
        return JoinStrings(path1, path2, path3);
    }

    public static string Join(string? path1, string? path2, string? path3, string? path4)
    {
        return JoinStrings(path1, path2, path3, path4);
    }

    public static string Join(params string?[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Join((ReadOnlySpan<string?>)paths);
    }

    public static string Join(params ReadOnlySpan<string?> paths)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            AppendJoined(builder, path.AsSpan());
        }

        return builder.ToString();
    }

    public static bool TryJoin(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (path1.IsEmpty && path2.IsEmpty)
        {
            return true;
        }

        if (path1.IsEmpty || path2.IsEmpty)
        {
            var pathToUse = path1.IsEmpty ? path2 : path1;
            if (destination.Length < pathToUse.Length)
            {
                return false;
            }

            pathToUse.CopyTo(destination);
            charsWritten = pathToUse.Length;
            return true;
        }

        var needsSeparator = !(EndsInDirectorySeparator(path1) || PathInternal.StartsWithDirectorySeparator(path2));
        var charsNeeded = path1.Length + path2.Length + (needsSeparator ? 1 : 0);
        if (destination.Length < charsNeeded)
        {
            return false;
        }

        path1.CopyTo(destination);
        if (needsSeparator)
        {
            destination[path1.Length] = DirectorySeparatorChar;
        }

        path2.CopyTo(destination.Slice(path1.Length + (needsSeparator ? 1 : 0)));
        charsWritten = charsNeeded;
        return true;
    }

    public static bool TryJoin(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3, Span<char> destination, out int charsWritten)
    {
        charsWritten = 0;
        if (path1.IsEmpty)
        {
            return TryJoin(path2, path3, destination, out charsWritten);
        }

        if (path2.IsEmpty)
        {
            return TryJoin(path1, path3, destination, out charsWritten);
        }

        if (path3.IsEmpty)
        {
            return TryJoin(path1, path2, destination, out charsWritten);
        }

        var firstSeparator = !(EndsInDirectorySeparator(path1) || PathInternal.StartsWithDirectorySeparator(path2));
        var secondSeparator = !(EndsInDirectorySeparator(path2) || PathInternal.StartsWithDirectorySeparator(path3));
        var charsNeeded = path1.Length + path2.Length + path3.Length + (firstSeparator ? 1 : 0) + (secondSeparator ? 1 : 0);
        if (destination.Length < charsNeeded)
        {
            return false;
        }

        path1.CopyTo(destination);
        var offset = path1.Length;
        if (firstSeparator)
        {
            destination[offset++] = DirectorySeparatorChar;
        }

        path2.CopyTo(destination.Slice(offset));
        offset += path2.Length;
        if (secondSeparator)
        {
            destination[offset++] = DirectorySeparatorChar;
        }

        path3.CopyTo(destination.Slice(offset));
        charsWritten = charsNeeded;
        return true;
    }

    public static string? GetPathRoot(string? path)
    {
        if (PathInternal.IsEffectivelyEmpty(path))
        {
            return null;
        }

        return IsPathRooted(path) ? PathInternal.DirectorySeparatorCharAsString : string.Empty;
    }

    public static ReadOnlySpan<char> GetPathRoot(ReadOnlySpan<char> path) =>
        IsPathRooted(path) ? PathInternal.DirectorySeparatorCharAsString.AsSpan() : ReadOnlySpan<char>.Empty;

    public static bool IsPathRooted([NotNullWhen(true)] string? path) => path is not null && IsPathRooted(path.AsSpan());

    public static bool IsPathRooted(ReadOnlySpan<char> path) => PathInternal.StartsWithDirectorySeparator(path);

    public static string GetRelativePath(string relativeTo, string path)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        ArgumentNullException.ThrowIfNull(path);
        if (PathInternal.IsEffectivelyEmpty(relativeTo.AsSpan()))
        {
            throw new ArgumentException("The path is empty.", nameof(relativeTo));
        }

        if (PathInternal.IsEffectivelyEmpty(path.AsSpan()))
        {
            throw new ArgumentException("The path is empty.", nameof(path));
        }

        // The desktop implementation resolves both values against the current directory.
        // NetWasm deliberately has no ambient current directory, so normalize lexical
        // segments in place and retain relative-vs-rooted identity.
        relativeTo = NormalizeRelativePath(relativeTo);
        path = NormalizeRelativePath(path);

        if (!PathInternal.AreRootsEqual(relativeTo, path))
        {
            return path;
        }

        var commonLength = PathInternal.GetCommonPathLength(relativeTo, path);
        if (commonLength == 0)
        {
            return path;
        }

        var relativeToLength = relativeTo.Length;
        if (EndsInDirectorySeparator(relativeTo.AsSpan()))
        {
            relativeToLength--;
        }

        var pathEndsInSeparator = EndsInDirectorySeparator(path.AsSpan());
        var pathLength = path.Length;
        if (pathEndsInSeparator)
        {
            pathLength--;
        }

        if (relativeToLength == pathLength && commonLength >= relativeToLength)
        {
            return ".";
        }

        var builder = new StringBuilder(Math.Max(relativeTo.Length, path.Length));
        if (commonLength < relativeToLength)
        {
            builder.Append("..");
            for (var index = commonLength + 1; index < relativeToLength; index++)
            {
                if (PathInternal.IsDirectorySeparator(relativeTo[index]))
                {
                    builder.Append(DirectorySeparatorChar);
                    builder.Append("..");
                }
            }
        }
        else if (commonLength < path.Length && PathInternal.IsDirectorySeparator(path[commonLength]))
        {
            commonLength++;
        }

        var differenceLength = pathLength - commonLength;
        if (pathEndsInSeparator)
        {
            differenceLength++;
        }

        if (differenceLength > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(DirectorySeparatorChar);
            }

            builder.Append(path.AsSpan(commonLength, differenceLength));
        }

        return builder.Length == 0 ? "." : builder.ToString();
    }

    public static string TrimEndingDirectorySeparator(string path) => PathInternal.TrimEndingDirectorySeparator(path)!;

    public static ReadOnlySpan<char> TrimEndingDirectorySeparator(ReadOnlySpan<char> path) => PathInternal.TrimEndingDirectorySeparator(path);

    public static bool EndsInDirectorySeparator(ReadOnlySpan<char> path) => PathInternal.EndsInDirectorySeparator(path);

    public static bool EndsInDirectorySeparator([NotNullWhen(true)] string? path) => PathInternal.EndsInDirectorySeparator(path);

    private static string CombineInternal(string first, string second)
    {
        if (first.Length == 0)
        {
            return second;
        }

        if (second.Length == 0 || IsPathRooted(second.AsSpan()))
        {
            return second.Length == 0 ? first : second;
        }

        return JoinNonEmpty(first.AsSpan(), second.AsSpan());
    }

    private static string JoinNonEmpty(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2)
    {
        var builder = new StringBuilder();
        if (!path1.IsEmpty)
        {
            AppendJoined(builder, path1);
        }

        if (!path2.IsEmpty)
        {
            AppendJoined(builder, path2);
        }

        return builder.ToString();
    }

    private static string JoinNonEmpty(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3)
    {
        var builder = JoinNonEmpty(path1, path2);
        return path3.IsEmpty ? builder : JoinNonEmpty(builder.AsSpan(), path3);
    }

    private static string JoinNonEmpty(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3, ReadOnlySpan<char> path4)
    {
        var builder = JoinNonEmpty(path1, path2, path3);
        return path4.IsEmpty ? builder : JoinNonEmpty(builder.AsSpan(), path4);
    }

    private static string JoinStrings(string? path1, string? path2, string? path3 = null, string? path4 = null)
    {
        var builder = new StringBuilder();
        AppendJoinedIfPresent(builder, path1);
        AppendJoinedIfPresent(builder, path2);
        AppendJoinedIfPresent(builder, path3);
        AppendJoinedIfPresent(builder, path4);
        return builder.ToString();
    }

    private static void AppendJoinedIfPresent(StringBuilder builder, string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            AppendJoined(builder, path.AsSpan());
        }
    }

    private static void AppendJoined(StringBuilder builder, ReadOnlySpan<char> path)
    {
        if (builder.Length == 0)
        {
            builder.Append(path);
            return;
        }

        if (!PathInternal.IsDirectorySeparator(builder[^1]) && !PathInternal.IsDirectorySeparator(path[0]))
        {
            builder.Append(DirectorySeparatorChar);
        }

        builder.Append(path);
    }

    private static string NormalizeRelativePath(string path)
    {
        var rooted = IsPathRooted(path.AsSpan());
        var trailingSeparator = EndsInDirectorySeparator(path.AsSpan());
        var segments = new System.Collections.Generic.List<string>();
        var segmentStart = rooted ? 1 : 0;

        for (var index = segmentStart; index <= path.Length; index++)
        {
            if (index < path.Length && !PathInternal.IsDirectorySeparator(path[index]))
            {
                continue;
            }

            if (index > segmentStart)
            {
                var segment = path.Substring(segmentStart, index - segmentStart);
                if (segment == ".")
                {
                    // no-op
                }
                else if (segment == "..")
                {
                    if (segments.Count > 0 && segments[^1] != "..")
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }
                    else if (!rooted)
                    {
                        segments.Add(segment);
                    }
                }
                else
                {
                    segments.Add(segment);
                }
            }

            segmentStart = index + 1;
        }

        var builder = new StringBuilder(path.Length);
        if (rooted)
        {
            builder.Append(DirectorySeparatorChar);
        }

        for (var index = 0; index < segments.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(DirectorySeparatorChar);
            }

            builder.Append(segments[index]);
        }

        if (trailingSeparator && builder.Length > (rooted ? 1 : 0))
        {
            builder.Append(DirectorySeparatorChar);
        }

        return builder.Length == 0 ? "." : builder.ToString();
    }
}
