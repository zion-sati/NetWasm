using System;

namespace NetWasm.Hosting.Deployment;

internal static class DeploymentFunctionName
{
    private const string ConstructorPrefix = "[constructor]";
    private const string ExportResourceDropPrefix = "[export-resource-drop]";
    private const string ExportResourceNewPrefix = "[export-resource-new]";
    private const string ExportResourceRepPrefix = "[export-resource-rep]";
    private const string MethodPrefix = "[method]";
    private const string ResourceDestructorPrefix = "[resource-dtor]";
    private const string ResourceDropPrefix = "[resource-drop]";
    private const string StaticPrefix = "[static]";

    public static bool IsCanonical(string? value)
    {
        if (value is null)
        {
            return false;
        }

        if (IsToken(value))
        {
            return true;
        }

        var intrinsic = ReadIntrinsic(value);
        if (!intrinsic.IsEmpty)
        {
            return IsWitIdentifier(intrinsic);
        }

        var member = value.StartsWith(MethodPrefix, StringComparison.Ordinal)
            ? value.AsSpan(MethodPrefix.Length)
            : value.StartsWith(StaticPrefix, StringComparison.Ordinal)
                ? value.AsSpan(StaticPrefix.Length)
                : default;
        if (member.IsEmpty)
        {
            return false;
        }

        var separator = member.IndexOf('.');
        return separator > 0
            && separator < member.Length - 1
            && member[(separator + 1)..].IndexOf('.') < 0
            && IsWitIdentifier(member[..separator])
            && IsWitIdentifier(member[(separator + 1)..]);
    }

    private static ReadOnlySpan<char> ReadIntrinsic(string value)
    {
        foreach (var prefix in new[]
                 {
                     ConstructorPrefix,
                     ExportResourceDropPrefix,
                     ExportResourceNewPrefix,
                     ExportResourceRepPrefix,
                     ResourceDestructorPrefix,
                     ResourceDropPrefix,
                 })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value.AsSpan(prefix.Length);
            }
        }

        return default;
    }

    private static bool IsToken(ReadOnlySpan<char> value) =>
        IsIdentifier(value, allowDot: true);

    private static bool IsWitIdentifier(ReadOnlySpan<char> value) =>
        IsIdentifier(value, allowDot: false);

    private static bool IsIdentifier(ReadOnlySpan<char> value, bool allowDot)
    {
        if (value.IsEmpty || !IsTokenStart(value[0]))
        {
            return false;
        }

        foreach (var character in value[1..])
        {
            if (!IsTokenStart(character) && character != '-' && (!allowDot || character != '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenStart(char value) =>
        char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value);
}
