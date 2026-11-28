// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NetWasm.Compiler.Analysis.CompositeFormats;

namespace NetWasm.Compiler.Tests;

// The parser reads a composite format as desktop .NET's string.Format reads it, which the compiler's own runtime is:
// it accepts what CompositeFormat.Parse accepts, counts the arguments CompositeFormat says the format needs, and marks
// the arguments string.Format hands an item format to, and which of those formats are not hexadecimal. Past an index
// or alignment of seven digits CompositeFormat has no limit where string.Format has one, checked on its own.
public sealed partial class CompositeFormatParserTests
{
    // Pieces whose combinations cover text, escapes, holes with every part and the ways each part goes wrong.
    internal static readonly string[] Pieces =
    [
        "", "a", " ", "{{", "}}", "{", "}", "{0}", "{1}", "{2}", "{0 }", "{ 0}", "{0,3}", "{0,-3}", "{0 ,3}", "{0, 3}",
        "{0,3 }", "{0,}", "{0,-}", "{0,- 3}", "{0,x}", "{0:X}", "{0:x2}", "{1:X8}", "{0:X100}", "{0:N2}", "{2:D}",
        "{0:}", "{0,4:X2}", "{0 , -4 :x}", "{0:{}", "{0:}}", "{0:a b}", "{0", "{0,", "{0:", "{a}", "{-1}", "{0x}",
        "{00}", "{01}", "{12}", "{9999999}", "{10000000}", "{0,9999999}", "{0,10000000}", "{0,1000000:X}",
    ];

    [Fact]
    public void ReadsFormatsAsDesktopStringFormatDoes()
    {
        var checkedFormats = 0;
        foreach (var first in Pieces)
        {
            foreach (var second in Pieces)
            {
                foreach (var third in new[] { "", "z", "{1}", "{0:x}", "}" })
                {
                    Check(first + second + third);
                    checkedFormats++;
                }
            }
        }
        Assert.True(checkedFormats > 10_000);
    }

    // string.Format reads an index or alignment to the first of its digits that reaches a million, so seven digits
    // parse and eight do not: what it formats with ten million arguments, and what it rejects.
    [Fact]
    public void ReadsIndexAndAlignmentLimitsAsStringFormatDoes()
    {
        var arguments = new object?[10_000_000];
        foreach (var format in new[] { "{9999999}", "{10000000}", "{0,9999999}", "{0,-9999999}", "{0,10000000}", "{0,1000000:X}" })
        {
            bool formats;
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, format, arguments);
                formats = true;
            }
            catch (FormatException)
            {
                formats = false;
            }
            Assert.True(formats == CompositeFormatParser.TryParse(format, out _, out _, out _, out _), format);
        }
    }

    // The format TypedCompositeFormat reads: the text as written, doubled braces included, and each hole as '{', one
    // character of 0x100 + index + 8 * (alignment + 2048), the item format and '}'. An index from eight or an alignment
    // past -2048 to 2047 has no encoding, and its call keeps string.Format.
    [Theory]
    [InlineData("", "")]
    [InlineData("a{{b}}", "a{{b}}")]
    [InlineData("a{0}b", "a{\u4100}b")]
    [InlineData("{{{1,-3:x2}}}", "{{{\u40E9x2}}}")]
    [InlineData("{0 , 4 :X}|{0:}|{0: a b }", "{\u4120X}|{\u4100}|{\u4100 a b }")]
    [InlineData("\u00FC{2}\uD834\uDD1E{0,1}", "\u00FC{\u4102}\uD834\uDD1E{\u4108}")]
    [InlineData("{7,2047}{0,-2048}", "{\u80FF}{\u0100}")]
    [InlineData("{0,2048}", null)]
    [InlineData("{0,-2049}", null)]
    [InlineData("a{8}", null)]
    public void EncodesHolesAsTypedCompositeFormatReadsThem(string format, string? encoded)
    {
        Assert.True(CompositeFormatParser.TryParse(format, out _, out _, out _, out var actual));
        Assert.Equal(encoded, actual);
    }

    private static void Check(string format)
    {
        if (SevenDigits().IsMatch(format))
        {
            return;
        }
        CompositeFormat? composite;
        try
        {
            composite = CompositeFormat.Parse(format);
        }
        catch (FormatException)
        {
            composite = null;
        }
        var parsed = CompositeFormatParser.TryParse(format, out var count, out var itemFormatted, out var otherThanHex, out _);
        Assert.True(parsed == composite is not null, $"'{format}': parsed {parsed}, desktop {composite is not null}");
        if (composite is null)
        {
            return;
        }
        Assert.True(composite.MinimumArgumentCount == count, $"'{format}': {count} arguments, desktop {composite.MinimumArgumentCount}");
        // Formatting needs as many arguments as the format indexes, and padding to a million spaces is no check.
        if (count > 3 || WideAlignment().IsMatch(format))
        {
            return;
        }
        var probes = Enumerable.Range(0, count).Select(static index => new Probe()).ToArray();
        _ = string.Format(CultureInfo.InvariantCulture, format, probes);
        for (var index = 0; index < count; index++)
        {
            var formats = probes[index].Formats;
            var bit = 1u << index;
            Assert.True(formats.Any(static item => item is not null) == ((itemFormatted & bit) != 0),
                $"'{format}': argument {index} item formats [{string.Join(",", formats)}]");
            Assert.True(formats.Any(static item => item is not null && !Hexadecimal().IsMatch(item)) == ((otherThanHex & bit) != 0),
                $"'{format}': argument {index} non-hexadecimal item formats [{string.Join(",", formats)}]");
        }
    }

    [GeneratedRegex(@"\d{7,}")]
    private static partial Regex SevenDigits();

    [GeneratedRegex(@",\s*-?\d{4,}")]
    private static partial Regex WideAlignment();

    [GeneratedRegex("^[Xx][0-9]{0,2}$")]
    private static partial Regex Hexadecimal();

    // Records the item format string.Format passes each time it formats the probe.
    private sealed class Probe : IFormattable
    {
        public List<string?> Formats { get; } = [];

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            Formats.Add(format);
            return "p";
        }
    }
}
