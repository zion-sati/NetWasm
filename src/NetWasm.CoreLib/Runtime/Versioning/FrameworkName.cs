// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic port of dotnet/runtime System.Private.CoreLib FrameworkName.cs.
// Upstream commit: 474ed963699fc29809015392738a15231aaae2de.
// Reflection and culture-specific services are not required by this managed parser.

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.Versioning;

public sealed class FrameworkName : IEquatable<FrameworkName?>
{
    private readonly string _identifier;
    private readonly Version _version;
    private readonly string _profile;
    private string? _fullName;

    private const char ComponentSeparator = ',';
    private const char KeyValueSeparator = '=';
    private const char VersionValuePrefix = 'v';
    private const string VersionKey = "Version";
    private const string ProfileKey = "Profile";

    public string Identifier
    {
        get => _identifier;
    }

    public Version Version
    {
        get => _version;
    }

    public string Profile
    {
        get => _profile;
    }

    public string FullName
    {
        get
        {
            _fullName ??= string.IsNullOrEmpty(Profile)
                ? $"{Identifier},Version=v{Version}"
                : $"{Identifier},Version=v{Version},Profile={Profile}";
            return _fullName;
        }
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as FrameworkName);

    public bool Equals([NotNullWhen(true)] FrameworkName? other) =>
        other is not null && Identifier == other.Identifier && Version == other.Version && Profile == other.Profile;

    public override int GetHashCode() => Identifier.GetHashCode() ^ Version.GetHashCode() ^ Profile.GetHashCode();

    public override string ToString() => FullName;

    public FrameworkName(string identifier, Version version)
        : this(identifier, version, null)
    {
    }

    public FrameworkName(string identifier, Version version, string? profile)
    {
        identifier = identifier?.Trim()!;
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentNullException.ThrowIfNull(version);
        _identifier = identifier;
        _version = version;
        _profile = profile is null ? string.Empty : profile.Trim();
    }

    // Parses: "<identifier>, Version=[v|V]<version>, Profile=<profile>".
    // Identifier and version are required; profile is optional and components may
    // appear in either order after the identifier.
    public FrameworkName(string frameworkName)
    {
        ArgumentException.ThrowIfNullOrEmpty(frameworkName);
        ReadOnlySpan<char> frameworkNameSpan = frameworkName;
        Span<Range> components = stackalloc Range[4];
        Span<char> componentSeparator = stackalloc char[1];
        componentSeparator[0] = ComponentSeparator;
        var componentCount = frameworkNameSpan.Split(components, componentSeparator);
        if (componentCount is not (2 or 3))
        {
            throw InvalidFrameworkName(frameworkName);
        }
        components = components.Slice(0, componentCount);

        _identifier = CreateString(frameworkNameSpan[components[0]].Trim());
        if (_identifier.Length == 0)
        {
            throw InvalidFrameworkName(frameworkName);
        }

        _profile = string.Empty;
        _version = null!;
        var versionFound = false;
        for (var index = 1; index < components.Length; index++)
        {
            var component = frameworkNameSpan[components[index]];
            var separatorIndex = component.IndexOf(KeyValueSeparator);
            if (separatorIndex < 0 || separatorIndex != component.LastIndexOf(KeyValueSeparator))
            {
                throw InvalidFrameworkName(frameworkName);
            }

            var key = component[..separatorIndex].Trim();
            var value = component[(separatorIndex + 1)..].Trim();
            if (key.Equals(VersionKey, StringComparison.OrdinalIgnoreCase))
            {
                versionFound = true;
                if (value.Length > 0 && (value[0] == VersionValuePrefix || value[0] == 'V'))
                {
                    value = value[1..];
                }

                _version = ParseVersion(value, frameworkName);
            }
            else if (key.Equals(ProfileKey, StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0)
                {
                    _profile = CreateString(value);
                }
            }
            else
            {
                throw InvalidFrameworkName(frameworkName);
            }
        }

        if (!versionFound)
        {
            throw new ArgumentException("Framework name is missing its Version component.", nameof(frameworkName));
        }
    }

    private static string CreateString(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        var result = new string('\0', value.Length);
        value.CopyTo(new Span<char>(ref result.GetRawStringData(), result.Length));
        return result;
    }

    private static Version ParseVersion(ReadOnlySpan<char> value, string frameworkName)
    {
        try
        {
            return Version.Parse(value);
        }
        catch (Exception exception)
        {
            throw new ArgumentException("Framework name contains an invalid version.", nameof(frameworkName), exception);
        }
    }

    private static ArgumentException InvalidFrameworkName(string frameworkName) =>
        new("Framework name is invalid.", nameof(frameworkName));

    public static bool operator ==(FrameworkName? left, FrameworkName? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(FrameworkName? left, FrameworkName? right) => !(left == right);
}
