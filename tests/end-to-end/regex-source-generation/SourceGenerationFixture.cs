using System.Text.RegularExpressions;

namespace NetWasm.Tests.RegexSourceGeneration.Fixture;

internal static partial class GeneratedPatterns
{
    [GeneratedRegex(
        "^(?<word>[A-Z][a-z]+)-(?<digits>\\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    internal static partial Regex Token();

    [GeneratedRegex("^(?<pair>[a-z]{2})-\\k<pair>$", RegexOptions.CultureInvariant)]
    internal static partial Regex Backreference();

    [GeneratedRegex("(?<delimiter>[,;])", RegexOptions.CultureInvariant)]
    internal static partial Regex Delimiter();

    [GeneratedRegex("\\p{Lu}+", RegexOptions.CultureInvariant)]
    internal static partial Regex Uppercase();

    [GeneratedRegex("^item:(.+)$", RegexOptions.Multiline | RegexOptions.Singleline)]
    internal static partial Regex MultilineItem();

    [GeneratedRegex("\\d+", RegexOptions.RightToLeft | RegexOptions.CultureInvariant)]
    internal static partial Regex LastDigits();

    [GeneratedRegex("(?<number>\\d+)", RegexOptions.CultureInvariant)]
    internal static partial Regex Numbers();
}

internal static partial class GenericContainer<T>
    where T : class
{
    [GeneratedRegex("^[a-z]+$", RegexOptions.CultureInvariant)]
    internal static partial Regex Word();
}

public static class EntryPoint
{
    public static int Run(int input) => input switch
    {
        0 => MatchToken(),
        1 => MatchBackreference(),
        2 => SplitAndReplace(),
        3 => EnumerateMatches(),
        4 => MatchUnicodeAndOptions(),
        5 => MatchRightToLeft(),
        6 => GenericContainer<string>.Word().IsMatch("generated") ? 601 : -6,
        _ => GeneratedPatterns.Token().IsMatch("invalid") ? -7 : 701,
    };

    private static int MatchToken()
    {
        Match match = GeneratedPatterns.Token().Match("Alpha-42");
        return match.Success &&
            match.Groups["word"].Value == "Alpha" &&
            match.Groups["digits"].Value == "42"
            ? 101
            : -1;
    }

    private static int MatchBackreference()
    {
        var match = GeneratedPatterns.Backreference().Match("ab-ab");
        return match.Success && match.Value == "ab-ab" && !match.NextMatch().Success &&
            !GeneratedPatterns.Backreference().IsMatch("xx-xy")
            ? 201
            : -2;
    }

    private static int SplitAndReplace()
    {
        Regex regex = GeneratedPatterns.Delimiter();
        string[] pieces = regex.Split("a,b;c");
        string replaced = regex.Replace("a,b;c", "<$1>");
        int splitLength = 0;
        foreach (Range range in regex.EnumerateSplits("a,b;c"))
        {
            splitLength += "a,b;c".AsSpan(range).Length;
        }

        return pieces.Length == 5 &&
            pieces[0] == "a" && pieces[1] == "," && pieces[2] == "b" &&
            pieces[3] == ";" && pieces[4] == "c" &&
            replaced == "a<,>b<;>c" && splitLength == 3
            ? 301
            : -3;
    }

    private static int EnumerateMatches()
    {
        const string value = "a1 b22 c333";
        Regex regex = GeneratedPatterns.Numbers();
        MatchCollection matches = regex.Matches(value);
        Match first = matches[0];
        Match second = first.NextMatch();
        int footprint = 0;
        foreach (ValueMatch match in regex.EnumerateMatches(value))
        {
            footprint += match.Index * 10 + match.Length;
        }

        return regex.Count(value) == 3 && matches.Count == 3 &&
            first.Value == "1" && second.Value == "22" && footprint == 136
            ? 401
            : -4;
    }

    private static int MatchUnicodeAndOptions()
    {
        bool unicode = GeneratedPatterns.Uppercase().IsMatch("éΩz");
        Match multiline = GeneratedPatterns.MultilineItem().Match("skip\nitem:a\nb");
        return unicode && multiline.Success && multiline.Groups[1].Value == "a\nb"
            ? 501
            : -5;
    }

    private static int MatchRightToLeft()
    {
        Match match = GeneratedPatterns.LastDigits().Match("a12b345");
        return match.Success && match.Index == 4 && match.Value == "345" ? 551 : -55;
    }
}
