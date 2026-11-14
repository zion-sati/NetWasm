using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;
using RegexType = System.Text.RegularExpressions.Regex;

namespace NetWasm.System.Text.RegularExpressions.Tests;

public sealed partial class RegexTests
{
    [Test]
    public async Task InterpretedMatchingPreservesCapturesAndGroupMetadata()
    {
        var regex = new RegexType(@"^(?<word>[a-z]+)-\k<word>$");
        var match = regex.Match("alpha-alpha");

        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("alpha-alpha");
        await Assert.That(match.Groups["word"].Value).IsEqualTo("alpha");
        await Assert.That(match.Groups["word"].Captures.Count).IsEqualTo(1);

        var names = regex.GetGroupNames();
        var numbers = regex.GetGroupNumbers();
        await Assert.That(names.Length).IsEqualTo(2);
        await Assert.That(numbers.Length).IsEqualTo(2);
        await Assert.That(regex.GroupNumberFromName("word")).IsEqualTo(1);
        await Assert.That(regex.GroupNameFromNumber(1)).IsEqualTo("word");
    }

    [Test]
    public async Task InterpretedReplacementAndSplitPreserveCapturedText()
    {
        var replacement = RegexType.Replace(
            "Doe, Jane; Roe, John",
            @"(?<last>\w+), (\w+)",
            "$1 ${last}");
        var parts = RegexType.Split("a, b;c", @"(,\s*|;)");

        await Assert.That(replacement).IsEqualTo("Jane Doe; John Roe");
        await Assert.That(parts.Length).IsEqualTo(5);
        await Assert.That(parts[0]).IsEqualTo("a");
        await Assert.That(parts[1]).IsEqualTo(", ");
        await Assert.That(parts[2]).IsEqualTo("b");
        await Assert.That(parts[3]).IsEqualTo(";");
        await Assert.That(parts[4]).IsEqualTo("c");

        var evaluated = RegexType.Replace("a1b22", @"\d+", match => $"[{match.Value.Length}]");
        await Assert.That(evaluated).IsEqualTo("a[1]b[2]");
    }

    [Test]
    public async Task InterpretedMatchAndSplitEnumeratorsExposeStableSpans()
    {
        const string input = "a1 b22 c333";
        var regex = new RegexType(@"\d+");
        var matches = regex.Matches(input);
        var footprint = MatchFootprint(regex, input);
        var splitFootprint = SplitFootprint(new RegexType("[,;]"), "a,bb;ccc");

        await Assert.That(matches.Count).IsEqualTo(3);
        await Assert.That(matches[0].Value).IsEqualTo("1");
        await Assert.That(matches[1].Index).IsEqualTo(4);
        await Assert.That(regex.Count(input)).IsEqualTo(3);
        await Assert.That(footprint).IsEqualTo(136);
        await Assert.That(splitFootprint).IsEqualTo(6);
    }

    [Test]
    public async Task EmptyMatchesAdvanceAndCountWithoutRepeatingPositions()
    {
        var regex = new RegexType("(?=a)");
        var matches = regex.Matches("baaa");
        var first = regex.Match("baaa");
        var second = first.NextMatch();
        var third = second.NextMatch();

        await Assert.That(matches.Count).IsEqualTo(3);
        await Assert.That(regex.Count("baaa")).IsEqualTo(3);
        await Assert.That(first.Index).IsEqualTo(1);
        await Assert.That(second.Index).IsEqualTo(2);
        await Assert.That(third.Index).IsEqualTo(3);
        await Assert.That(third.NextMatch().Success).IsFalse();
    }

    [Test]
    public async Task UnicodeClassesCaseFoldingAndEcmaOptionsRemainInvariant()
    {
        var uppercase = RegexType.Match("abc ΑΒΓ 123", @"\p{Lu}+");
        var consonants = RegexType.Match("aeioubcdf", "[a-z-[aeiou]]+");
        var insensitive = new RegexType("café", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var ecma = new RegexType(@"\w+", RegexOptions.ECMAScript | RegexOptions.CultureInvariant);

        await Assert.That(uppercase.Value).IsEqualTo("ΑΒΓ");
        await Assert.That(consonants.Value).IsEqualTo("bcdf");
        await Assert.That(insensitive.IsMatch("CAFÉ")).IsTrue();
        await Assert.That(insensitive.IsMatch("CAFE")).IsFalse();
        await Assert.That(ecma.Match("abc_123").Length).IsEqualTo(7);
    }

    [Test]
    public async Task RightToLeftAndAbsoluteAnchorsSelectExpectedMatches()
    {
        var rightToLeft = new RegexType("one", RegexOptions.RightToLeft);
        var match = rightToLeft.Match("one two one");
        var anchored = new RegexType(@"\A\w+\z", RegexOptions.CultureInvariant);

        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Index).IsEqualTo(8);
        await Assert.That(match.NextMatch().Index).IsEqualTo(0);
        await Assert.That(anchored.IsMatch("abc123")).IsTrue();
        await Assert.That(anchored.IsMatch("abc 123")).IsFalse();
    }

    [Test]
    public async Task ParseErrorsAndTimeoutArgumentsRemainDeterministic()
    {
        RegexParseException? parseError = null;
        try
        {
            _ = new RegexType("a(");
        }
        catch (RegexParseException exception)
        {
            parseError = exception;
        }

        var timeoutRejected = false;
        try
        {
            _ = new RegexType("a", RegexOptions.None, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            timeoutRejected = true;
        }

        await Assert.That(parseError is not null).IsTrue();
        await Assert.That(parseError!.Offset >= 0).IsTrue();
        await Assert.That(timeoutRejected).IsTrue();
    }

    [Test]
    public async Task GenericCallersPreserveValueAndReferenceSpecializations()
    {
        var valueTypeMatch = MatchFor<int>();
        var referenceTypeMatch = MatchFor<string>();

        await Assert.That(valueTypeMatch).IsEqualTo("123");
        await Assert.That(referenceTypeMatch).IsEqualTo("word");
    }

    [Test]
    public async Task SourceGeneratedRunnersPreserveCapturesAndBackreferences()
    {
        var token = GeneratedPatterns.Token().Match("Alpha-42");
        var backreference = GeneratedPatterns.Backreference().Match("ab-ab");

        await Assert.That(token.Success).IsTrue();
        await Assert.That(token.Groups["word"].Value).IsEqualTo("Alpha");
        await Assert.That(token.Groups["digits"].Value).IsEqualTo("42");
        await Assert.That(backreference.Success).IsTrue();
        await Assert.That(backreference.Value).IsEqualTo("ab-ab");
        await Assert.That(backreference.NextMatch().Success).IsFalse();
    }

    [Test]
    public async Task SourceGeneratedReplacementSplitAndSplitEnumerationRemainAligned()
    {
        var regex = GeneratedPatterns.Delimiter();
        var parts = regex.Split("a,b;c");
        var replaced = regex.Replace("a,b;c", "<$1>");
        var splitFootprint = SplitFootprint(regex, "a,b;c");

        await Assert.That(parts.Length).IsEqualTo(5);
        await Assert.That(parts[1]).IsEqualTo(",");
        await Assert.That(parts[3]).IsEqualTo(";");
        await Assert.That(replaced).IsEqualTo("a<,>b<;>c");
        await Assert.That(splitFootprint).IsEqualTo(3);
    }

    [Test]
    public async Task SourceGeneratedMatchEnumerationAndCountRemainStable()
    {
        const string input = "a1 b22 c333";
        var regex = GeneratedPatterns.Numbers();
        var matches = regex.Matches(input);

        await Assert.That(regex.Count(input)).IsEqualTo(3);
        await Assert.That(matches.Count).IsEqualTo(3);
        await Assert.That(matches[0].Value).IsEqualTo("1");
        await Assert.That(matches[1].Value).IsEqualTo("22");
        await Assert.That(MatchFootprint(regex, input)).IsEqualTo(136);
    }

    [Test]
    public async Task SourceGeneratedOptionsAndGenericCallersRemainPortable()
    {
        var uppercase = GeneratedPatterns.Uppercase().IsMatch("éΩz");
        var multiline = GeneratedPatterns.MultilineItem().Match("skip\nitem:a\nb");
        var rightToLeft = GeneratedPatterns.LastDigits().Match("a12b345");
        var generic = GeneratedContainer<string>.Word().IsMatch("generated");

        await Assert.That(uppercase).IsTrue();
        await Assert.That(multiline.Success).IsTrue();
        await Assert.That(multiline.Groups[1].Value).IsEqualTo("a\nb");
        await Assert.That(rightToLeft.Index).IsEqualTo(4);
        await Assert.That(rightToLeft.Value).IsEqualTo("345");
        await Assert.That(generic).IsTrue();
    }

    [Test]
    public async Task SourceGeneratedFallbacksUseQualifiedManagedEngines()
    {
        var nonBacktracking = GeneratedFallbackPatterns.NonBacktracking();
        var caseInsensitive = GeneratedFallbackPatterns.IgnoreCaseBackreference();

        await Assert.That(nonBacktracking.IsMatch("aaaa")).IsTrue();
        await Assert.That(nonBacktracking.IsMatch("b")).IsFalse();
        await Assert.That(caseInsensitive.IsMatch("ab-AB")).IsTrue();
    }

    [Test]
    public async Task GroupCollectionLookupEnumeratesPresentAndMissingNames()
    {
        var groups = RegexType.Match("a1", "(?<letter>[a-z])(?<digit>\\d)").Groups;
        var found = groups.TryGetValue("letter", out var letter);
        var missing = groups.TryGetValue("missing", out var absent);
        var keyCount = 0;
        foreach (var key in groups.Keys)
        {
            keyCount++;
        }

        await Assert.That(found).IsTrue();
        await Assert.That(letter!.Value).IsEqualTo("a");
        await Assert.That(missing).IsFalse();
        await Assert.That(absent).IsNull();
        await Assert.That(groups.ContainsKey("digit")).IsTrue();
        await Assert.That(groups.ContainsKey("missing")).IsFalse();
        await Assert.That(keyCount).IsEqualTo(3);
    }

    [Test]
    public async Task StaticCacheSupportsDisabledShrunkAndRandomizedEvictionModes()
    {
        var originalSize = RegexType.CacheSize;
        try
        {
            RegexType.CacheSize = 0;
            await Assert.That(RegexType.IsMatch("cache", "cache")).IsTrue();

            RegexType.CacheSize = 3;
            await Assert.That(RegexType.IsMatch("a", "a")).IsTrue();
            await Assert.That(RegexType.IsMatch("b", "b")).IsTrue();
            await Assert.That(RegexType.IsMatch("c", "c")).IsTrue();
            RegexType.CacheSize = 1;
            await Assert.That(RegexType.IsMatch("a", "a")).IsTrue();

            RegexType.CacheSize = 40;
            for (var index = 0; index < 36; index++)
            {
                await Assert.That(RegexType.IsMatch($"value-{index}", $"value-{index}")).IsTrue();
            }

            await Assert.That(RegexType.IsMatch("cached", "cached", RegexOptions.IgnoreCase)).IsTrue();
            await Assert.That(RegexType.IsMatch("CACHED", "cached", RegexOptions.IgnoreCase)).IsTrue();
        }
        finally
        {
            RegexType.CacheSize = originalSize;
        }
    }

    [Test]
    public async Task InterpretedRunnerHandlesStringSpanAndEmptyMatchBoundaries()
    {
        var regex = new RegexType("a+");
        var bounded = regex.Match("zaa", 1, 2);
        var noMatch = regex.Match("zzz");
        var rightToLeft = new RegexType("a*", RegexOptions.RightToLeft);
        var empty = rightToLeft.Match("bbb");

        await Assert.That(bounded.Success).IsTrue();
        await Assert.That(bounded.Value).IsEqualTo("aa");
        await Assert.That(noMatch.Success).IsFalse();
        await Assert.That(empty.Success).IsTrue();
        await Assert.That(empty.Length).IsEqualTo(0);
        await Assert.That(regex.IsMatch("aa")).IsTrue();
    }

    [Test]
    public async Task SimpleReplacementCoversMatchesNoMatchesCountsAndRightToLeft()
    {
        var regex = new RegexType("a");
        var all = regex.Replace("aba", "x");
        var limited = regex.Replace("aba", "x", 1, 0);
        var noMatch = regex.Replace("bbb", "x");
        var rightToLeft = new RegexType("a", RegexOptions.RightToLeft).Replace("aba", "x");

        await Assert.That(all).IsEqualTo("xbx");
        await Assert.That(limited).IsEqualTo("xba");
        await Assert.That(noMatch).IsEqualTo("bbb");
        await Assert.That(rightToLeft).IsEqualTo("xbx");
    }

    [Test]
    public async Task CharacterClassesPrefixesAndWordBoundariesSelectExpectedText()
    {
        var ranges = new RegexType("[a-f]+");
        var negated = new RegexType("[^0-9]+");
        var prefix = new RegexType("needle");
        var boundary = new RegexType(@"\bword\B");

        await Assert.That(ranges.Match("zzabcfg").Value).IsEqualTo("abcf");
        await Assert.That(negated.Match("123text!").Value).IsEqualTo("text!");
        await Assert.That(prefix.Match("find needle here").Index).IsEqualTo(5);
        await Assert.That(boundary.IsMatch("wordx")).IsTrue();
        await Assert.That(boundary.IsMatch("swordx")).IsFalse();
    }

    [Test]
    public async Task NonBacktrackingAnchorsLoopsAlternationAndCapturesRemainPortable()
    {
        var anchors = new RegexType(@"\A(?<word>\w+)\z", RegexOptions.NonBacktracking);
        var alternation = new RegexType(@"\A(?:a|b){2,4}\z", RegexOptions.NonBacktracking);
        var lineEnd = new RegexType(@"a\Z", RegexOptions.NonBacktracking);
        var boundaries = new RegexType(@"\b\w+\B", RegexOptions.NonBacktracking);

        var anchored = anchors.Match("word");
        await Assert.That(anchored.Success).IsTrue();
        await Assert.That(anchored.Groups["word"].Value).IsEqualTo("word");
        await Assert.That(alternation.IsMatch("abba")).IsTrue();
        await Assert.That(alternation.IsMatch("abcde")).IsFalse();
        await Assert.That(lineEnd.IsMatch("a\n")).IsTrue();
        await Assert.That(boundaries.IsMatch("wordx")).IsTrue();
    }

    [Test]
    public async Task ParseAndOptionFailuresUseStableFormattedMessages()
    {
        RegexParseException? undefinedGroup = null;
        RegexParseException? unterminatedClass = null;
        try
        {
            _ = new RegexType(@"\k<missing>");
        }
        catch (RegexParseException exception)
        {
            undefinedGroup = exception;
        }

        try
        {
            _ = new RegexType("[");
        }
        catch (RegexParseException exception)
        {
            unterminatedClass = exception;
        }

        var invalidOptions = false;
        try
        {
            _ = new RegexType("a", RegexOptions.NonBacktracking | RegexOptions.RightToLeft);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidOptions = true;
        }

        await Assert.That(undefinedGroup).IsNotNull();
        await Assert.That(unterminatedClass).IsNotNull();
        await Assert.That(invalidOptions).IsTrue();
    }

    [Test]
    public async Task ConstructorsExposeOptionsAndTimeoutAndRejectInvalidCacheSize()
    {
        var originalCacheSize = RegexType.CacheSize;
        try
        {
            var regex = new RegexType(
                "word",
                RegexOptions.IgnoreCase | RegexOptions.RightToLeft,
                TimeSpan.FromSeconds(1));
            var compiled = new RegexType("word", RegexOptions.Compiled);
            var rejected = false;

            try
            {
                RegexType.CacheSize = -1;
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            await Assert.That(regex.Options).IsEqualTo(RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            await Assert.That(regex.MatchTimeout).IsEqualTo(TimeSpan.FromSeconds(1));
            await Assert.That(regex.RightToLeft).IsTrue();
            await Assert.That(compiled.IsMatch("word")).IsTrue();
            await Assert.That(rejected).IsTrue();
        }
        finally
        {
            RegexType.CacheSize = originalCacheSize;
        }
    }

    [Test]
    public async Task SpanAndRangeApisReturnExpectedBoundaries()
    {
        var regex = new RegexType("[a-z]+");
        var spanResults = MeasureSpanApis(regex);

        await Assert.That(spanResults.Success).IsTrue();
        await Assert.That(spanResults.Index).IsEqualTo(2);
        await Assert.That(spanResults.Length).IsEqualTo(5);
        await Assert.That(spanResults.MatchCount).IsEqualTo(2);
        await Assert.That(spanResults.RangeCount).IsEqualTo(4);
    }

    [Test]
    public async Task CapturesCanBeCopiedAndEnumeratedThroughCollectionContracts()
    {
        var match = new RegexType("(?<letter>[a-z])+").Match("abc");
        var groups = match.Groups;
        var captures = groups["letter"].Captures;
        var copiedGroups = new Group[groups.Count];
        var copiedCaptures = new Capture[captures.Count];
        var groupCount = 0;
        var captureCount = 0;

        groups.CopyTo(copiedGroups, 0);
        captures.CopyTo(copiedCaptures, 0);
        foreach (var _ in groups)
        {
            groupCount++;
        }

        foreach (var _ in captures)
        {
            captureCount++;
        }

        await Assert.That(copiedGroups[0].Value).IsEqualTo("abc");
        await Assert.That(copiedCaptures[0].Value).IsEqualTo("a");
        await Assert.That(captureCount).IsEqualTo(3);
        await Assert.That(groupCount).IsEqualTo(2);
        await Assert.That(groups.IsReadOnly).IsTrue();
        await Assert.That(captures.IsReadOnly).IsTrue();
    }

    [Test]
    public async Task ReplacementTokensPreservePrefixSuffixAndLiteralText()
    {
        var input = "left:42:right";
        var replacement = RegexType.Replace(input, "(?<value>\\d+)", "$$[$&]-$1-${value}-$`-$'");
        var noMatch = RegexType.Replace("plain", "\\d+", "$$");

        await Assert.That(replacement).IsEqualTo("left:$[42]-42-42-left:-:right:right");
        await Assert.That(noMatch).IsEqualTo("plain");
    }

    [Test]
    public async Task NonBacktrackingCapturesAndEnumerationRemainConsistent()
    {
        var regex = new RegexType(
            @"(?<word>[a-z]+)-(?<number>\d+)",
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        var match = regex.Match("one-1 two-22");
        var matches = regex.Matches("one-1 two-22");

        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("one-1");
        await Assert.That(match.Groups["word"].Value).IsEqualTo("one");
        await Assert.That(match.Groups["number"].Value).IsEqualTo("1");
        await Assert.That(matches.Count).IsEqualTo(2);
        await Assert.That(matches[1].Groups["number"].Value).IsEqualTo("22");
    }

    [Test]
    public async Task NonBacktrackingUnicodeAnchorsAndBoundariesRemainPortable()
    {
        var uppercase = new RegexType(@"\A\p{Lu}+\z", RegexOptions.NonBacktracking);
        var whitespace = new RegexType(@"\s+", RegexOptions.NonBacktracking);
        var boundary = new RegexType(@"\bword\B", RegexOptions.NonBacktracking);
        var lineEnd = new RegexType(@"item\Z", RegexOptions.NonBacktracking);

        await Assert.That(uppercase.IsMatch("ABC")).IsTrue();
        await Assert.That(uppercase.IsMatch("AbC")).IsFalse();
        await Assert.That(whitespace.Match("x \t y").Value).IsEqualTo(" \t ");
        await Assert.That(boundary.IsMatch("wordx")).IsTrue();
        await Assert.That(boundary.IsMatch("swordx")).IsFalse();
        await Assert.That(lineEnd.IsMatch("item\n")).IsTrue();
    }

    [Test]
    public async Task NonBacktrackingStateGrowthPreservesAlternationSemantics()
    {
        var alternatives = new string[96];
        for (var index = 0; index < alternatives.Length; index++)
        {
            alternatives[index] = $"token{index}";
        }

        var regex = new RegexType(
            $"\\A(?:{string.Join("|", alternatives)})\\z",
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

        await Assert.That(regex.IsMatch("token0")).IsTrue();
        await Assert.That(regex.IsMatch("token95")).IsTrue();
        await Assert.That(regex.IsMatch("token96")).IsFalse();
    }

    [Test]
    public async Task CharacterClassAndParseFailuresRetainStablePublicBehavior()
    {
        var classRegex = new RegexType("[A-F0-9-[A-C]]+");
        RegexParseException? rangeError = null;
        try
        {
            _ = new RegexType("[a-\\d]");
        }
        catch (RegexParseException exception)
        {
            rangeError = exception;
        }

        var hasStableMessage = rangeError?.Message.Contains("Cannot include class") == true;
        await Assert.That(classRegex.Match("ABCDEF012").Value).IsEqualTo("DEF012");
        await Assert.That(classRegex.IsMatch("ABC")).IsFalse();
        await Assert.That(rangeError).IsNotNull();
        await Assert.That(hasStableMessage).IsTrue();
    }

    private static string MatchFor<T>()
    {
        var pattern = typeof(T) == typeof(int) ? @"\d+" : @"\w+";
        return new RegexType(pattern).Match(typeof(T) == typeof(int) ? "x123y" : "word-42").Value;
    }

    private static (bool Success, int Index, int Length, int MatchCount, int RangeCount) MeasureSpanApis(RegexType regex)
    {
        var success = regex.IsMatch("--alpha beta--".AsSpan(), 2);
        var matchCount = 0;
        var rangeCount = 0;

        foreach (var _ in regex.EnumerateMatches("--alpha beta--".AsSpan()))
        {
            matchCount++;
        }

        foreach (var _ in regex.EnumerateSplits("a,b,c".AsSpan()))
        {
            rangeCount++;
        }

        var match = regex.Match("--alpha beta--", 2);
        return (success, match.Index, match.Length, matchCount, rangeCount);
    }

    private static int MatchFootprint(RegexType regex, string input)
    {
        var footprint = 0;
        foreach (var match in regex.EnumerateMatches(input))
        {
            footprint += match.Index * 10 + match.Length;
        }

        return footprint;
    }

    private static int SplitFootprint(RegexType regex, string input)
    {
        var footprint = 0;
        foreach (var range in regex.EnumerateSplits(input))
        {
            footprint += input.AsSpan(range).Length;
        }

        return footprint;
    }

    private static partial class GeneratedPatterns
    {
        [GeneratedRegex(
            @"^(?<word>[A-Z][a-z]+)-(?<digits>\d{2})$",
            RegexOptions.CultureInvariant)]
        public static partial RegexType Token();

        [GeneratedRegex(@"^(?<pair>[a-z]{2})-\k<pair>$", RegexOptions.CultureInvariant)]
        public static partial RegexType Backreference();

        [GeneratedRegex(@"(?<delimiter>[,;])", RegexOptions.CultureInvariant)]
        public static partial RegexType Delimiter();

        [GeneratedRegex(@"\p{Lu}+", RegexOptions.CultureInvariant)]
        public static partial RegexType Uppercase();

        [GeneratedRegex(@"^item:(.+)$", RegexOptions.Multiline | RegexOptions.Singleline)]
        public static partial RegexType MultilineItem();

        [GeneratedRegex(@"\d+", RegexOptions.RightToLeft | RegexOptions.CultureInvariant)]
        public static partial RegexType LastDigits();

        [GeneratedRegex(@"(?<number>\d+)", RegexOptions.CultureInvariant)]
        public static partial RegexType Numbers();
    }

    private static partial class GeneratedContainer<T>
        where T : class
    {
        [GeneratedRegex("^[a-z]+$", RegexOptions.CultureInvariant)]
        public static partial RegexType Word();
    }

    private static partial class GeneratedFallbackPatterns
    {
        [GeneratedRegex("^(a|aa)+$", RegexOptions.NonBacktracking)]
        public static partial RegexType NonBacktracking();

        [GeneratedRegex("^(?<pair>ab)-\\k<pair>$", RegexOptions.IgnoreCase)]
        public static partial RegexType IgnoreCaseBackreference();
    }
}
