using System.Text.RegularExpressions;

namespace NetWasm.Tests.RegexSourceGeneration.FallbackFixture;

internal static partial class GeneratedFallbackPatterns
{
    [GeneratedRegex("^(a|aa)+$", RegexOptions.NonBacktracking)]
    internal static partial Regex NonBacktracking();

    [GeneratedRegex("^(?<pair>ab)-\\k<pair>$", RegexOptions.IgnoreCase)]
    internal static partial Regex IgnoreCaseBackreference();
}

public static class EntryPoint
{
    public static int Run(int input) => input switch
    {
        0 => GeneratedFallbackPatterns.NonBacktracking().IsMatch("aaaa") ? 101 : -1,
        1 => GeneratedFallbackPatterns.IgnoreCaseBackreference().IsMatch("ab-AB")
            ? 201
            : -2,
        _ => GeneratedFallbackPatterns.NonBacktracking().IsMatch("b") ? -3 : 301,
    };
}
