// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// Names the parameters of a generated JavaScript or TypeScript function after
/// the source parameters. A name that is not a usable identifier, is reserved,
/// repeats, or collides with a name the generated code uses becomes
/// <c>argument{index}</c>.
/// </summary>
internal static partial class JavaScriptParameterNames
{
    private static readonly FrozenSet<string> ReservedWords = new[]
    {
        "arguments", "await", "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "enum", "eval", "export", "extends", "false", "finally",
        "for", "function", "if", "implements", "import", "in", "instanceof", "interface", "let",
        "new", "null", "package", "private", "protected", "public", "return", "static", "super",
        "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
        "with", "yield",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static ImmutableArray<string> Resolve(
        IReadOnlyList<string> sourceNames,
        int count,
        IEnumerable<string> unavailable)
    {
        var taken = new HashSet<string>(unavailable, StringComparer.Ordinal);
        var names = new string?[count];
        for (var index = 0; index < count; index++)
        {
            var name = index < sourceNames.Count ? sourceNames[index] : null;
            if (name is not null && IsUsable(name) && taken.Add(name)) names[index] = name;
        }
        for (var index = 0; index < count; index++)
        {
            if (names[index] is not null) continue;
            var fallback = $"argument{index}";
            while (!taken.Add(fallback)) fallback = "_" + fallback;
            names[index] = fallback;
        }
        return [.. names!];
    }

    /// <summary>Converts a kebab-case WIT name to camelCase.</summary>
    public static string FromWit(string name)
    {
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select((part, index) => index == 0
            ? part
            : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool IsUsable(string name) => Identifier().IsMatch(name) && !ReservedWords.Contains(name);

    [GeneratedRegex(@"^[\p{L}_$][\p{L}\p{Nd}_$]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
