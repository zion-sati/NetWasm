// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Linq;

namespace NetWasm.Hosting;

// The version of a versioned WIT identity such as wasi:http/types@0.3.2: one exact semantic version, MAJOR.MINOR.PATCH
// without leading zeros, with the prerelease and build metadata a WIT package version may carry
// (wasi:tls@0.3.0-draft). The JavaScript hosts check the same grammar (exact-semantic-version.mjs).
internal static class ExactSemanticVersion
{
    public static bool IsValid(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var build = version.IndexOf('+', StringComparison.Ordinal);
        if (build >= 0 && !IsIdentifierList(version[(build + 1)..]))
        {
            return false;
        }

        var withoutBuild = build < 0 ? version : version[..build];
        var prerelease = withoutBuild.IndexOf('-', StringComparison.Ordinal);
        if (prerelease >= 0 && !IsIdentifierList(withoutBuild[(prerelease + 1)..]))
        {
            return false;
        }

        var numbers = (prerelease < 0 ? withoutBuild : withoutBuild[..prerelease]).Split('.');
        return numbers.Length == 3 && numbers.All(IsNumber);
    }

    private static bool IsNumber(string value) =>
        value.Length != 0 && value.All(char.IsAsciiDigit) && (value.Length == 1 || value[0] != '0');

    // Dot-separated identifiers of ASCII letters, digits and hyphens.
    private static bool IsIdentifierList(string value) =>
        value.Split('.').All(part => part.Length != 0 && part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
}
