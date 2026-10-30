// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 474ed963699fc29809015392738a15231aaae2de). Only deterministic lexical path
// operations are retained; ambient-current-directory, existence, temporary-file,
// and other filesystem operations are intentionally outside NetWasm.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace System.IO;

public static class Path
{
    private const int KeyLength = 8;

    [Obsolete("Path.InvalidPathChars has been deprecated. Use GetInvalidPathChars or GetInvalidFileNameChars instead.")]
    public static readonly char[] InvalidPathChars = GetInvalidPathChars();

    public static readonly char DirectorySeparatorChar = PathInternal.DirectorySeparatorChar;
    public static readonly char AltDirectorySeparatorChar = PathInternal.AltDirectorySeparatorChar;
    public static readonly char VolumeSeparatorChar = PathInternal.VolumeSeparatorChar;
    public static readonly char PathSeparator = PathInternal.PathSeparator;

    public static char[] GetInvalidFileNameChars() => ['\0', '/'];

    public static char[] GetInvalidPathChars() => ['\0'];

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

    public static unsafe string GetRandomFileName()
    {
        var key = stackalloc byte[KeyLength];
        Security.Cryptography.RandomNumberGeneratorImplementation.FillSpan(
            new Span<byte>(key, KeyLength));
        return string.Create(
            12,
            (IntPtr)key,
            static (characters, state) =>
                Populate83FileNameFromRandomBytes((byte*)state, KeyLength, characters));
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
        return CombineInternal(path1, path2, path3);
    }

    public static string Combine(string path1, string path2, string path3, string path4)
    {
        ArgumentNullException.ThrowIfNull(path1);
        ArgumentNullException.ThrowIfNull(path2);
        ArgumentNullException.ThrowIfNull(path3);
        ArgumentNullException.ThrowIfNull(path4);
        return CombineInternal(path1, path2, path3, path4);
    }

    public static string Combine(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Combine((ReadOnlySpan<string>)paths);
    }

    public static string Combine(params ReadOnlySpan<string> paths)
    {
        var maximumLength = 0;
        var firstComponent = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(paths[index], nameof(paths));
            if (paths[index].Length == 0)
            {
                continue;
            }

            if (IsPathRooted(paths[index]))
            {
                firstComponent = index;
                maximumLength = paths[index].Length;
            }
            else
            {
                maximumLength += paths[index].Length;
            }

            if (!PathInternal.IsDirectorySeparator(paths[index][^1]))
                maximumLength++;
        }

        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(maximumLength);
        for (var index = firstComponent; index < paths.Length; index++)
        {
            if (paths[index].Length == 0)
                continue;

            if (builder.Length != 0 && !PathInternal.IsDirectorySeparator(builder[^1]))
                builder.Append(DirectorySeparatorChar);

            builder.Append(paths[index]);
        }

        return builder.ToString();
    }

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2)
    {
        if (path1.IsEmpty)
            return path2.ToString();
        if (path2.IsEmpty)
            return path1.ToString();
        return JoinInternal(path1, path2);
    }

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3)
    {
        if (path1.IsEmpty)
            return Join(path2, path3);
        if (path2.IsEmpty)
            return Join(path1, path3);
        if (path3.IsEmpty)
            return Join(path1, path2);
        return JoinInternal(path1, path2, path3);
    }

    public static string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3, ReadOnlySpan<char> path4)
    {
        if (path1.IsEmpty)
            return Join(path2, path3, path4);
        if (path2.IsEmpty)
            return Join(path1, path3, path4);
        if (path3.IsEmpty)
            return Join(path1, path2, path4);
        if (path4.IsEmpty)
            return Join(path1, path2, path3);
        return JoinInternal(path1, path2, path3, path4);
    }

    public static string Join(string? path1, string? path2)
    {
        if (string.IsNullOrEmpty(path1))
            return path2 ?? string.Empty;
        if (string.IsNullOrEmpty(path2))
            return path1!;
        return JoinInternal(path1, path2);
    }

    public static string Join(string? path1, string? path2, string? path3)
    {
        if (string.IsNullOrEmpty(path1))
            return Join(path2, path3);
        if (string.IsNullOrEmpty(path2))
            return Join(path1, path3);
        if (string.IsNullOrEmpty(path3))
            return Join(path1, path2);
        return JoinInternal(path1, path2, path3);
    }

    public static string Join(string? path1, string? path2, string? path3, string? path4)
    {
        if (string.IsNullOrEmpty(path1))
            return Join(path2, path3, path4);
        if (string.IsNullOrEmpty(path2))
            return Join(path1, path3, path4);
        if (string.IsNullOrEmpty(path3))
            return Join(path1, path2, path4);
        if (string.IsNullOrEmpty(path4))
            return Join(path1, path2, path3);
        return JoinInternal(path1, path2, path3, path4);
    }

    public static string Join(params string?[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Join((ReadOnlySpan<string?>)paths);
    }

    public static string Join(params ReadOnlySpan<string?> paths)
    {
        if (paths.IsEmpty)
            return string.Empty;

        var maximumLength = paths.Length - 1;
        foreach (var path in paths)
            maximumLength += path?.Length ?? 0;

        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(maximumLength);
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            if (string.IsNullOrEmpty(path))
                continue;

            if (builder.Length != 0 &&
                !PathInternal.IsDirectorySeparator(builder[^1]) &&
                !PathInternal.IsDirectorySeparator(path![0]))
            {
                builder.Append(DirectorySeparatorChar);
            }

            builder.Append(path);
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

    public static string GetFullPath(string path, string basePath)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(basePath);
        if (!IsPathFullyQualified(basePath))
            throw new ArgumentException("The base path is not fully qualified.", nameof(basePath));
        if (basePath.Contains('\0') || path.Contains('\0'))
            throw new ArgumentException("Null characters are not allowed in paths.");

        return GetFullPathInternal(
            IsPathFullyQualified(path) ? path : CombineInternal(basePath, path));
    }

    private static string GetFullPathInternal(string path)
    {
        var collapsed = PathInternal.RemoveRelativeSegments(
            path,
            PathInternal.GetRootLength(path.AsSpan()));
        return collapsed.Length == 0 ? PathInternal.DirectorySeparatorCharAsString : collapsed;
    }

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

        if (IsPathFullyQualified(relativeTo) && IsPathFullyQualified(path))
        {
            relativeTo = GetFullPathInternal(relativeTo);
            path = GetFullPathInternal(path);
        }
        else
        {
            // Relative inputs have no ambient current directory in the NetWasm profile.
            // Preserve the established deterministic lexical behavior instead.
            relativeTo = NormalizeRelativePath(relativeTo);
            path = NormalizeRelativePath(path);
        }

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

        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(Math.Max(relativeTo.Length, path.Length));
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

        if (builder.Length == 0)
        {
            builder.Dispose();
            return ".";
        }

        return builder.ToString();
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

        return JoinInternal(first.AsSpan(), second.AsSpan());
    }

    private static string CombineInternal(string first, string second, string third)
    {
        if (first.Length == 0)
            return CombineInternal(second, third);
        if (second.Length == 0)
            return CombineInternal(first, third);
        if (third.Length == 0)
            return CombineInternal(first, second);
        if (IsPathRooted(third.AsSpan()))
            return third;
        if (IsPathRooted(second.AsSpan()))
            return CombineInternal(second, third);
        return JoinInternal(first.AsSpan(), second.AsSpan(), third.AsSpan());
    }

    private static string CombineInternal(string first, string second, string third, string fourth)
    {
        if (first.Length == 0)
            return CombineInternal(second, third, fourth);
        if (second.Length == 0)
            return CombineInternal(first, third, fourth);
        if (third.Length == 0)
            return CombineInternal(first, second, fourth);
        if (fourth.Length == 0)
            return CombineInternal(first, second, third);
        if (IsPathRooted(fourth.AsSpan()))
            return fourth;
        if (IsPathRooted(third.AsSpan()))
            return CombineInternal(third, fourth);
        if (IsPathRooted(second.AsSpan()))
            return CombineInternal(second, third, fourth);
        return JoinInternal(first.AsSpan(), second.AsSpan(), third.AsSpan(), fourth.AsSpan());
    }

    private static string JoinInternal(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        var needsSeparator = !PathInternal.IsDirectorySeparator(first[^1]) &&
            !PathInternal.IsDirectorySeparator(second[0]);
        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(first.Length + second.Length + (needsSeparator ? 1 : 0));
        builder.Append(first);
        if (needsSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(second);
        return builder.ToString();
    }

    private static string JoinInternal(ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
    {
        var firstSeparator = !PathInternal.IsDirectorySeparator(first[^1]) &&
            !PathInternal.IsDirectorySeparator(second[0]);
        var secondSeparator = !PathInternal.IsDirectorySeparator(second[^1]) &&
            !PathInternal.IsDirectorySeparator(third[0]);
        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(
            first.Length + second.Length + third.Length +
            (firstSeparator ? 1 : 0) + (secondSeparator ? 1 : 0));
        builder.Append(first);
        if (firstSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(second);
        if (secondSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(third);
        return builder.ToString();
    }

    private static string JoinInternal(
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third,
        ReadOnlySpan<char> fourth)
    {
        var firstSeparator = !PathInternal.IsDirectorySeparator(first[^1]) &&
            !PathInternal.IsDirectorySeparator(second[0]);
        var secondSeparator = !PathInternal.IsDirectorySeparator(second[^1]) &&
            !PathInternal.IsDirectorySeparator(third[0]);
        var thirdSeparator = !PathInternal.IsDirectorySeparator(third[^1]) &&
            !PathInternal.IsDirectorySeparator(fourth[0]);
        var builder = new ValueStringBuilder(stackalloc char[260]);
        builder.EnsureCapacity(
            first.Length + second.Length + third.Length + fourth.Length +
            (firstSeparator ? 1 : 0) + (secondSeparator ? 1 : 0) +
            (thirdSeparator ? 1 : 0));
        builder.Append(first);
        if (firstSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(second);
        if (secondSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(third);
        if (thirdSeparator)
            builder.Append(DirectorySeparatorChar);
        builder.Append(fourth);
        return builder.ToString();
    }

    private static ReadOnlySpan<byte> Base32Characters =>
        "abcdefghijklmnopqrstuvwxyz012345"u8;

    internal static unsafe void Populate83FileNameFromRandomBytes(
        byte* bytes,
        int byteCount,
        Span<char> characters)
    {
        var byte0 = bytes[0];
        var byte1 = bytes[1];
        var byte2 = bytes[2];
        var byte3 = bytes[3];
        var byte4 = bytes[4];

        characters[11] = (char)Base32Characters[bytes[7] & 0x1F];
        characters[0] = (char)Base32Characters[byte0 & 0x1F];
        characters[1] = (char)Base32Characters[byte1 & 0x1F];
        characters[2] = (char)Base32Characters[byte2 & 0x1F];
        characters[3] = (char)Base32Characters[byte3 & 0x1F];
        characters[4] = (char)Base32Characters[byte4 & 0x1F];
        characters[5] = (char)Base32Characters[
            ((byte0 & 0xE0) >> 5) | ((byte3 & 0x60) >> 2)];
        characters[6] = (char)Base32Characters[
            ((byte1 & 0xE0) >> 5) | ((byte4 & 0x60) >> 2)];

        byte2 >>= 5;
        if ((byte3 & 0x80) != 0)
            byte2 |= 0x08;
        if ((byte4 & 0x80) != 0)
            byte2 |= 0x10;

        characters[7] = (char)Base32Characters[byte2];
        characters[8] = '.';
        characters[9] = (char)Base32Characters[bytes[5] & 0x1F];
        characters[10] = (char)Base32Characters[bytes[6] & 0x1F];
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
