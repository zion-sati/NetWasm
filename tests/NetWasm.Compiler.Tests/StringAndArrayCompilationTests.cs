using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class StringAndArrayCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void SzArrayInterfaceIndexersPreserveArgumentExceptions(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"SzArrayInterfaceBounds_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;

            namespace SzArrayInterfaceBounds;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new[] { input };
                    IList<int> list = values;
                    IReadOnlyList<int> readOnlyList = values;
                    var expectedFailures = 0;

                    try { _ = list[-1]; }
                    catch (ArgumentOutOfRangeException) { expectedFailures++; }
                    try { _ = readOnlyList[1]; }
                    catch (ArgumentOutOfRangeException) { expectedFailures++; }
                    try { list[-1] = 1; }
                    catch (ArgumentOutOfRangeException) { expectedFailures++; }
                    try { list[1] = 1; }
                    catch (ArgumentOutOfRangeException) { expectedFailures++; }
                    try { _ = values[-1]; }
                    catch (IndexOutOfRangeException) { expectedFailures++; }

                    return expectedFailures == 5 && values[0] == input ? 42 : -1;
                }
            }
            """,
            "SzArrayInterfaceBounds.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OrdinalIgnoreCasePreservesUnicodeContracts(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"UnicodeOrdinalIgnoreCase_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;

            namespace UnicodeOrdinalIgnoreCase;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const string lower = "caf\u00e9-\u03c9";
                    const string upper = "CAF\u00c9-\u03a9";
                    var comparer = StringComparer.OrdinalIgnoreCase;

                    if (!comparer.Equals(lower, upper) || comparer.Compare(lower, upper) != 0)
                        return 1;
                    if (comparer.GetHashCode(lower) != comparer.GetHashCode(upper))
                        return 2;
                    if (!lower.Equals(upper, StringComparison.OrdinalIgnoreCase) ||
                        string.Compare(lower, upper, StringComparison.OrdinalIgnoreCase) != 0)
                        return 3;
                    if (lower.GetHashCode(StringComparison.OrdinalIgnoreCase) !=
                        upper.GetHashCode(StringComparison.OrdinalIgnoreCase))
                        return 4;
                    if ("--caf\u00e9-\u03c9--".IndexOf(upper, StringComparison.OrdinalIgnoreCase) != 2 ||
                        "caf\u00e9 CAF\u00c9".LastIndexOf('\u00e9', StringComparison.OrdinalIgnoreCase) != 8)
                        return 5;
                    if (!lower.StartsWith('C', StringComparison.OrdinalIgnoreCase) ||
                        !lower.EndsWith('\u03a9', StringComparison.OrdinalIgnoreCase))
                        return 6;
                    if (lower.ToUpperInvariant() != upper || upper.ToLowerInvariant() != lower)
                        return 7;
                    if (!lower.AsSpan().Equals(upper.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        return 8;

                    var haystack = "--caf\u00e9-\u03c9--CAF\u00c9-\u03a9--".AsSpan();
                    if (!haystack.Contains(upper.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                        !haystack.StartsWith("--CAF\u00c9".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                        !haystack.EndsWith("\u03c9--".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                        haystack.IndexOf(upper.AsSpan(), StringComparison.OrdinalIgnoreCase) != 2 ||
                        haystack.LastIndexOf(lower.AsSpan(), StringComparison.OrdinalIgnoreCase) != 10 ||
                        lower.AsSpan().CompareTo(upper.AsSpan(), StringComparison.OrdinalIgnoreCase) != 0)
                        return 9;

                    Span<char> destination = stackalloc char[lower.Length];
                    if (lower.AsSpan().ToUpperInvariant(destination) != lower.Length ||
                        !destination.SequenceEqual(upper.AsSpan()))
                        return 10;

                    var upperAscii = "ACCT-" + input.ToString();
                    var lowerAscii = "acct-" + input.ToString();
                    if (!object.ReferenceEquals(upperAscii, upperAscii.ToUpperInvariant()) ||
                        !object.ReferenceEquals(lowerAscii, lowerAscii.ToLowerInvariant()))
                        return 11;
                    if (char.ToUpperInvariant('a') != 'A' ||
                        char.ToUpperInvariant('A') != 'A' ||
                        char.ToUpperInvariant('[') != '[' ||
                        char.ToLowerInvariant('Z') != 'z' ||
                        char.ToLowerInvariant('z') != 'z' ||
                        char.ToLowerInvariant('@') != '@')
                        return 12;

                    var prefixUpperSource = "123abc";
                    var prefixUpperResult = prefixUpperSource.ToUpperInvariant();
                    var prefixLowerSource = "123ABC";
                    var prefixLowerResult = prefixLowerSource.ToLowerInvariant();
                    if (prefixUpperResult != "123ABC" || prefixUpperSource != "123abc" ||
                        object.ReferenceEquals(prefixUpperSource, prefixUpperResult) ||
                        prefixLowerResult != "123abc" || prefixLowerSource != "123ABC" ||
                        object.ReferenceEquals(prefixLowerSource, prefixLowerResult))
                        return 13;
                    if (!object.ReferenceEquals(string.Empty, string.Empty.ToUpperInvariant()) ||
                        !object.ReferenceEquals(string.Empty, string.Empty.ToLowerInvariant()))
                        return 14;

                    for (var code = 0; code < 128; code++)
                    {
                        var character = (char)code;
                        var expectedUpper = character is >= 'a' and <= 'z'
                            ? (char)(character - ('a' - 'A'))
                            : character;
                        var expectedLower = character is >= 'A' and <= 'Z'
                            ? (char)(character + ('a' - 'A'))
                            : character;
                        if (char.ToUpperInvariant(character) != expectedUpper ||
                            char.ToLowerInvariant(character) != expectedLower)
                            return 15;
                    }

                    const string supplementaryLower = "x\U00010428y";
                    const string supplementaryUpper = "X\U00010400Y";
                    if (!supplementaryLower.Equals(supplementaryUpper, StringComparison.OrdinalIgnoreCase) ||
                        string.Compare(supplementaryLower, supplementaryUpper, StringComparison.OrdinalIgnoreCase) != 0 ||
                        supplementaryLower.GetHashCode(StringComparison.OrdinalIgnoreCase) !=
                            supplementaryUpper.GetHashCode(StringComparison.OrdinalIgnoreCase) ||
                        !comparer.Equals(supplementaryLower, supplementaryUpper) ||
                        comparer.GetHashCode(supplementaryLower) != comparer.GetHashCode(supplementaryUpper))
                        return 16;
                    if ("--x\U00010428y--".IndexOf(supplementaryUpper, StringComparison.OrdinalIgnoreCase) != 2 ||
                        !supplementaryLower.AsSpan().Equals(supplementaryUpper.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                        supplementaryLower.AsSpan().CompareTo(supplementaryUpper.AsSpan(), StringComparison.OrdinalIgnoreCase) != 0)
                        return 17;
                    if (supplementaryLower.ToUpperInvariant() != supplementaryUpper ||
                        supplementaryUpper.ToLowerInvariant() != supplementaryLower)
                        return 18;

                    Span<char> supplementaryDestination = stackalloc char[supplementaryLower.Length];
                    if (supplementaryLower.AsSpan().ToUpperInvariant(supplementaryDestination) != supplementaryLower.Length ||
                        !supplementaryDestination.SequenceEqual(supplementaryUpper.AsSpan()))
                        return 19;

                    var supplementaryMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        [supplementaryLower] = 42,
                    };
                    if (!supplementaryMap.TryGetValue(supplementaryUpper, out var mappedValue) || mappedValue != 42)
                        return 20;

                    return input - input;
                }
            }
            """,
            "UnicodeOrdinalIgnoreCase.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    // Invariant casing skips unchanged ASCII four characters at a time. Each kind
    // of change, at every position after the letter-range bounds, must still map.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InvariantCasingMapsChangesAtEveryBlockPosition(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"InvariantCaseBlocks_{optimize}_{target}",
            """
            using System;
            using System.Text;

            namespace InvariantCaseBlocks;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    string[] upperChanges = ["a", "z", "é", "\U00010428", ""];
                    string[] lowerChanges = ["A", "Z", "É", "\U00010400", ""];
                    var checksum = input;
                    for (var length = 0; length < 12; length++)
                    {
                        for (var change = 0; change < upperChanges.Length; change++)
                        {
                            for (var position = 0; position <= length; position++)
                            {
                                checksum = checksum * 31 + Mix(Build("@AZ[`{0\u007f", length, position, upperChanges[change]), upper: true);
                                checksum = checksum * 31 + Mix(Build("@az[`{0\u007f", length, position, lowerChanges[change]), upper: false);
                            }
                        }
                    }
                    return checksum;
                }

                private static string Build(string filler, int length, int position, string change)
                {
                    var builder = new StringBuilder();
                    for (var index = 0; index < length; index++)
                    {
                        if (index == position) builder.Append(change);
                        builder.Append(filler[(index * 3 + length) % filler.Length]);
                    }
                    if (position == length) builder.Append(change);
                    return builder.ToString();
                }

                private static int Mix(string source, bool upper)
                {
                    var mapped = upper ? source.ToUpperInvariant() : source.ToLowerInvariant();
                    var hash = ReferenceEquals(source, mapped) ? 1 : 2;
                    foreach (var character in mapped) hash = hash * 33 + character;
                    return hash;
                }
            }
            """,
            "InvariantCaseBlocks.EntryPoint",
            optimize,
            target,
            41,
            []));

        // As desktop .NET computes it, with ICU or invariant globalization.
        Assert.Equal(214012633, result);
    }

    [Fact]
    public void CompilerExecutesStringBuilderGrowthInterpolationAndCompositeFormatting()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringBuilderFormattingFixture",
            """
            using System.Text;

            namespace StringBuilderFormattingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var builder = new StringBuilder(2);
                    builder.Append("ab");
                    builder.Append('c', 3);
                    builder.Append($"-{input:D2}");
                    builder.AppendFormat("-{0}-{1:X2}", "tail", input);
                    return builder.ToString() == "abccc-07-tail-07" ? input : 0;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StringBuilderFormattingFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(7, ExecuteWithNode(result.ApplicationModule, assets.Directory, 7));
    }

    [Fact]
    public void CompilerExecutesOrdinalTrimPadAndCharacterArrayOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PracticalStringFixture",
            """
            namespace PracticalStringFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var source = "\u2000\talpha\u3000";
                    var trimmed = source.Trim();
                    var characters = trimmed.PadLeft(7, '[').PadRight(8, ']').ToCharArray();
                    if (trimmed != "alpha" || characters.Length != 8 ||
                        characters[0] != '[' || characters[7] != ']' ||
                        "abca".LastIndexOf('a') != 3 || "..x..".Trim('.') != "x")
                        return -1;

                    const string whiteSpace =
                        "\u0009\u000a\u000b\u000c\u000d\u0020\u0085\u00a0\u1680" +
                        "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a" +
                        "\u2028\u2029\u202f\u205f\u3000";
                    for (var index = 0; index < whiteSpace.Length; index++)
                    {
                        var edge = whiteSpace[index].ToString();
                        if ((edge + "x" + edge).Trim() != "x")
                            return -2;
                    }

                    const string ordinary =
                        "\u0008\u000e\u001f\u0021\u0084\u0086\u009f\u00a1\u167f\u1681" +
                        "\u1fff\u200b\u2027\u202a\u202e\u2030\u205e\u2060\u2fff\u3001";
                    for (var index = 0; index < ordinary.Length; index++)
                    {
                        var edge = ordinary[index].ToString();
                        var value = edge + "x" + edge;
                        if (value.Trim() != value)
                            return -3;
                    }

                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PracticalStringFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesFoundationalOrdinalStringProfile()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "OrdinalStringCompilerFixture",
            """
            namespace OrdinalStringCompilerFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    string source = string.Concat("ab", "\0", "Ω", "\ud800");
                    int passed = 0;
                    if (source.Length == 5 && source[2] == '\0' && source[4] == '\ud800')
                        passed++;
                    if (source == string.Concat("a", "b\0Ω\ud800"))
                        passed++;
                    if (string.CompareOrdinal("a", "b") < 0 &&
                        string.CompareOrdinal(null, "a") < 0)
                        passed++;
                    if (source.Substring(1, 3) == "b\0Ω")
                        passed++;
                    if ("ac".Insert(1, "b") == "abc")
                        passed++;
                    if ("abcde".Remove(1, 3) == "ae")
                        passed++;
                    if ("abca".Replace('a', 'x') == "xbcx")
                        passed++;
                    if ("aaaa".Replace("aa", "b") == "bb")
                        passed++;
                    if (source.IndexOf('\0') == 2 &&
                        source.IndexOf("Ω", System.StringComparison.Ordinal) == 3)
                        passed++;
                    if (source.Contains("b\0", System.StringComparison.Ordinal) &&
                        source.StartsWith("ab", System.StringComparison.Ordinal) &&
                        source.EndsWith("Ω\ud800", System.StringComparison.Ordinal))
                        passed++;
                    string[] parts = "a,,b".Split(',');
                    if (parts.Length == 3 && parts[1] == string.Empty &&
                        string.Join("|", parts) == "a||b")
                        passed++;
                    if (source.GetHashCode() == string.Concat("ab\0", "Ω\ud800").GetHashCode())
                        passed++;
                    return input + passed;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "OrdinalStringCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(53, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OrdinalHashUsesBoundedPackedPairs(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"OrdinalPackedHash_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;

            namespace OrdinalPackedHash;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var defaultDictionary = new Dictionary<string, int>
                    {
                        ["identity"] = 7,
                    };
                    if (!ReferenceEquals(
                            defaultDictionary.Comparer,
                            EqualityComparer<string>.Default))
                        return 4;
                    var defaultFramed = "#identity@";
                    var defaultAlternate =
                        defaultDictionary.GetAlternateLookup<ReadOnlySpan<char>>();
                    if (!defaultAlternate.TryGetValue(
                            defaultFramed.AsSpan(1, 8),
                            out var defaultFound) ||
                        defaultFound != 7)
                        return 5;

                    var ordinalDictionary =
                        new Dictionary<string, int>(StringComparer.Ordinal);
                    if (!ReferenceEquals(ordinalDictionary.Comparer, StringComparer.Ordinal))
                        return 6;

                    var customComparer = new CountingComparer();
                    var customDictionary = new Dictionary<string, int>(customComparer)
                    {
                        ["custom"] = 11,
                    };
                    if (!ReferenceEquals(customDictionary.Comparer, customComparer) ||
                        !customDictionary.TryGetValue(
                            new string("custom".ToCharArray()),
                            out var customFound) ||
                        customFound != 11 ||
                        customComparer.HashCalls == 0 ||
                        customComparer.EqualityCalls == 0)
                        return 7;

                    var integerDictionary = new Dictionary<int, int>();
                    if (!ReferenceEquals(
                            integerDictionary.Comparer,
                            EqualityComparer<int>.Default))
                        return 8;

                    var boundaryValues = new[]
                    {
                        "",
                        "a",
                        "ab",
                        "abc",
                        "abcd",
                        "abcde",
                        "abcdef",
                        "abcdefg",
                        "abcdefgh",
                        "abcdefghi",
                        "\0",
                        "a\0",
                        "a\0b",
                        "Ω\ud800",
                        "\udc00Ω\0x",
                    };

                    foreach (var value in boundaryValues)
                    {
                        if (!Matches(value))
                            return 1;
                        if (!Matches(new string(value.ToCharArray())))
                            return 2;
                    }

                    var random = 101u;
                    for (var length = 0; length <= 64; length++)
                    {
                        var characters = new char[length];
                        for (var index = 0; index < characters.Length; index++)
                        {
                            random ^= random << 13;
                            random ^= random >> 17;
                            random ^= random << 5;
                            characters[index] = (char)random;
                        }
                        if (!Matches(new string(characters)))
                            return 3;
                    }

                    return input - input;
                }

                private static bool Matches(string value)
                {
                    var expected = Expected(value);
                    if (value.GetHashCode() != expected ||
                        StringComparer.Ordinal.GetHashCode(value) != expected)
                        return false;

                    var dictionary = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        [value] = 17,
                    };
                    var framed = "#" + value + "@";
                    var alternate = dictionary.GetAlternateLookup<ReadOnlySpan<char>>();
                    return alternate.TryGetValue(framed.AsSpan(1, value.Length), out var found) &&
                        found == 17;
                }

                private static int Expected(string value)
                {
                    var hash1 = (5381u << 16) + 5381u;
                    var hash2 = hash1;
                    var index = 0;
                    var remaining = value.Length;

                    while (remaining >= 4)
                    {
                        hash1 = (RotateLeft(hash1, 5) + hash1) ^ ReadPair(value, index);
                        hash2 = (RotateLeft(hash2, 5) + hash2) ^ ReadPair(value, index + 2);
                        index += 4;
                        remaining -= 4;
                    }

                    switch (remaining)
                    {
                        case 3:
                            hash1 = (RotateLeft(hash1, 5) + hash1) ^ ReadPair(value, index);
                            hash2 = (RotateLeft(hash2, 5) + hash2) ^ value[index + 2];
                            break;
                        case 2:
                            hash2 = (RotateLeft(hash2, 5) + hash2) ^ ReadPair(value, index);
                            break;
                        case 1:
                            hash2 = (RotateLeft(hash2, 5) + hash2) ^ value[index];
                            break;
                    }

                    return unchecked((int)(hash1 + (hash2 * 1_566_083_941)));
                }

                private static uint ReadPair(string value, int index) =>
                    value[index] | ((uint)value[index + 1] << 16);

                private static uint RotateLeft(uint value, int offset) =>
                    (value << offset) | (value >> (32 - offset));
            }

            public sealed class CountingComparer : IEqualityComparer<string>
            {
                public int HashCalls { get; private set; }
                public int EqualityCalls { get; private set; }

                public bool Equals(string? left, string? right)
                {
                    EqualityCalls++;
                    return string.Equals(left, right);
                }

                public int GetHashCode(string value)
                {
                    HashCalls++;
                    return value.GetHashCode();
                }
            }
            """,
            "OrdinalPackedHash.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompilerPreservesFoundationalStringExceptionsAndIdentityFastPaths()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "OrdinalStringBoundaryFixture",
            """
            namespace OrdinalStringBoundaryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    try { _ = "abc".Substring(-1); }
                    catch (System.ArgumentOutOfRangeException) { passed++; }
                    try { _ = "abc".Substring(2, 2); }
                    catch (System.ArgumentOutOfRangeException) { passed++; }
                    try { _ = "abc".IndexOf('a', -1); }
                    catch (System.ArgumentOutOfRangeException) { passed++; }
                    try { _ = "abc"[3]; }
                    catch (System.IndexOutOfRangeException) { passed++; }
                    try { _ = string.Concat((string?[])null!); }
                    catch (System.ArgumentNullException) { passed++; }
                    try { _ = "abc".Replace((string)null!, "x"); }
                    catch (System.ArgumentNullException) { passed++; }
                    try { _ = "abc".Replace("", "x"); }
                    catch (System.ArgumentException) { passed++; }
                    try
                    {
                        _ = "a".StartsWith(
                            "too long",
                            System.StringComparison.CurrentCulture);
                    }
                    catch (System.ArgumentException) { passed++; }
                    try { _ = System.StringComparer.InvariantCulture; }
                    catch (System.PlatformNotSupportedException) { passed++; }
                    try { _ = new string('x', -1); }
                    catch (System.ArgumentOutOfRangeException) { passed++; }
                    try { _ = new string('x', int.MaxValue); }
                    catch (System.OutOfMemoryException) { passed++; }
                    try
                    {
                        string? value = null;
                        _ = value!.Length;
                    }
                    catch (System.NullReferenceException) { passed++; }
                    try { _ = string.Join(",", (string?[])null!); }
                    catch (System.ArgumentNullException) { passed++; }

                    string source = "abc";
                    if (object.ReferenceEquals(string.Concat(null, null), string.Empty)) passed++;
                    if (object.ReferenceEquals(source.Substring(0), source)) passed++;
                    if (object.ReferenceEquals(source.Substring(3), string.Empty)) passed++;
                    if (object.ReferenceEquals(source.Replace("z", "x"), source)) passed++;
                    if (string.Concat([null, "a", null]) == "a") passed++;
                    if (string.Join(null, ["a", null]) == "a") passed++;
                    if ((int)System.StringComparison.CurrentCulture == 0 &&
                        (int)System.StringComparison.CurrentCultureIgnoreCase == 1 &&
                        (int)System.StringComparison.InvariantCulture == 2 &&
                        (int)System.StringComparison.InvariantCultureIgnoreCase == 3 &&
                        (int)System.StringComparison.Ordinal == 4 &&
                        (int)System.StringComparison.OrdinalIgnoreCase == 5) passed++;
                    string[] parts = ",a,".Split(',');
                    if (parts.Length == 3 && parts[0] == string.Empty &&
                        parts[2] == string.Empty) passed++;
                    return input + passed;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "OrdinalStringBoundaryFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(62, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerTerminatesStringSplitWithMultipleSeparatorsAtEndOfInput()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "MultipleSeparatorSplitFixture",
            """
            namespace MultipleSeparatorSplitFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    string[] unsplit = "Create".Split(',', ';');
                    string[] split = "a,b;c".Split(',', ';');
                    string[] trailing = "a,".Split(',', ';');
                    return unsplit.Length == 1 && unsplit[0] == "Create" &&
                        split.Length == 3 && split[0] == "a" && split[1] == "b" &&
                        split[2] == "c" && trailing.Length == 2 &&
                        trailing[0] == "a" && trailing[1] == string.Empty
                            ? input + 1
                            : 0;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MultipleSeparatorSplitFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersReferenceArraysWithAnExactTwoObjectLayout()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ArrayFixture",
            """
            namespace ArrayFixture;

            public sealed class Node
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new Node[2];
                    values[0] = new Node();
                    return values.Length + (values[0] == null ? 0 : input);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ArrayFixture.EntryPoint",
            "Run",
            []));
        using (var compilation = MetadataCompilationTestFactory.Load(
                   assembly,
                   [assets.CoreLib]))
        {
            var arrayType = MetadataCompilationTestActors.TypeFinder(compilation.Snapshot)
                .FindType("System.Array");

            Assert.Contains(result.Program.Methods.Values
                .SelectMany(method => method.Body.Instructions),
                instruction => instruction.Operation == CilOperation.NewArray);
            Assert.True(result.Layouts.ReferenceArrayTypeId > 0);
            var array = result.Layouts.ObjectLayouts[arrayType.Key];
            Assert.Equal(4 * sizeof(int), array.Size);
            Assert.True(array.ReferenceOffsets.AsSpan().SequenceEqual(
                [ManagedLayoutSnapshot.Wasm32ArrayDataPointerOffset]));
            Assert.True(result.ApplicationModule.AsSpan().IndexOf(
                "allocate_reference_array"u8) >= 0);
        }
    }

    [Fact]
    public void CompilerLowersDynamicallyAllocatedUtf16Strings()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DynamicStringFixture",
            """
            namespace DynamicStringFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    string value = new string('\u03a9', input);
                    return value.Length + value[0];
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DynamicStringFixture.EntryPoint",
            "Run",
            []));
        using (var compilation = MetadataCompilationTestFactory.Load(
                   assembly,
                   [assets.CoreLib]))
        {
            var stringType = MetadataCompilationTestActors.TypeFinder(compilation.Snapshot)
                .FindType("System.String").Key;
            var descriptor = result.Layouts.TypeDescriptors.Single(
                candidate => candidate.Type == stringType);

            Assert.Equal(2 * sizeof(int), descriptor.ObjectSize);
            Assert.True(result.Layouts.StringTypeId > 0);
            Assert.True(result.ApplicationModule.AsSpan().IndexOf("allocate_string"u8) >= 0);
        }
    }
}
