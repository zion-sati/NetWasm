using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Encodings.Web.Tests;

public sealed class EncodingTests
{
    [Test]
    public async Task DefaultJavaScriptEncoderEscapesHtmlAndUnicodeBoundaries()
    {
        var input = "A<>&\"'`\\é😀\n";
        var encoded = JavaScriptEncoder.Default.Encode(input);
        await Assert.That(encoded).IsEqualTo(
            "A\\u003C\\u003E\\u0026\\u0022\\u0027\\u0060\\\\\\u00E9\\uD83D\\uDE00\\n");
        await Assert.That(JavaScriptEncoder.Default.WillEncode(0x007F)).IsTrue();
        await Assert.That(JavaScriptEncoder.Default.WillEncode(0x0080)).IsTrue();
        await Assert.That(JavaScriptEncoder.Default.WillEncode(0x00E9)).IsTrue();
        await Assert.That(JavaScriptEncoder.Default.WillEncode(0x10000)).IsTrue();
    }

    [Test]
    public async Task UnsafeRelaxedEncoderPreservesUnicodeAndHtmlCharacters()
    {
        var encoded = JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode("A<>&\"'`\\é😀");

        await Assert.That(encoded).IsEqualTo("A<>&\\\"'`\\\\é\\uD83D\\uDE00");
        await Assert.That(encoded.Contains("\\\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(encoded.Contains("'`", StringComparison.Ordinal)).IsTrue();
        await Assert.That(encoded.Contains("\\\\", StringComparison.Ordinal)).IsTrue();
        await Assert.That(encoded.EndsWith("\\uD83D\\uDE00", StringComparison.Ordinal)).IsTrue();
        await Assert.That(JavaScriptEncoder.UnsafeRelaxedJsonEscaping.WillEncode(0x00E9)).IsFalse();
        await Assert.That(JavaScriptEncoder.UnsafeRelaxedJsonEscaping.WillEncode(0x10000)).IsTrue();
    }

    [Test]
    public async Task CustomRangesAndSettingsControlAllowedCharacters()
    {
        var settings = new TextEncoderSettings();
        settings.AllowCharacters('A', 'a', 'b', 'é');
        settings.ForbidCharacter('b');
        settings.AllowRange(UnicodeRange.Create('x', 'z'));

        var allowedCount = 0;
        var hasA = false;
        var hasLowerA = false;
        var hasEAcute = false;
        var hasX = false;
        var hasZ = false;
        var hasB = false;
        foreach (var codePoint in settings.GetAllowedCodePoints())
        {
            allowedCount++;
            hasA |= codePoint == 'A';
            hasLowerA |= codePoint == 'a';
            hasEAcute |= codePoint == 'é';
            hasX |= codePoint == 'x';
            hasZ |= codePoint == 'z';
            hasB |= codePoint == 'b';
        }
        var encoder = JavaScriptEncoder.Create(settings);
        var clone = JavaScriptEncoder.Create(new TextEncoderSettings(settings));

        await Assert.That(allowedCount).IsEqualTo(6);
        await Assert.That(hasA).IsTrue();
        await Assert.That(hasLowerA).IsTrue();
        await Assert.That(hasEAcute).IsTrue();
        await Assert.That(hasX).IsTrue();
        await Assert.That(hasZ).IsTrue();
        await Assert.That(hasB).IsFalse();
        await Assert.That(encoder.Encode("Aabéxyz")).IsEqualTo("Aa\\u0062éxyz");
        await Assert.That(clone.Encode("Aabéxyz")).IsEqualTo(encoder.Encode("Aabéxyz"));

        settings.Clear();
        var hasClearedCodePoint = false;
        foreach (var _ in settings.GetAllowedCodePoints())
        {
            hasClearedCodePoint = true;
            break;
        }
        await Assert.That(hasClearedCodePoint).IsFalse();
    }

    [Test]
    public async Task UnicodeRangesExposeStableBmpBoundaries()
    {
        await Assert.That(UnicodeRanges.None.FirstCodePoint).IsEqualTo(0);
        await Assert.That(UnicodeRanges.None.Length).IsEqualTo(0);
        await Assert.That(UnicodeRanges.BasicLatin.FirstCodePoint).IsEqualTo(0);
        await Assert.That(UnicodeRanges.BasicLatin.Length).IsEqualTo(128);
        await Assert.That(UnicodeRanges.All.FirstCodePoint).IsEqualTo(0);
        await Assert.That(UnicodeRanges.All.Length).IsEqualTo(65536);

        var range = UnicodeRange.Create('\u0100', '\u017F');
        await Assert.That(range.FirstCodePoint).IsEqualTo(0x0100);
        await Assert.That(range.Length).IsEqualTo(0x80);
        await Assert.That(ThrowsRange(() => { _ = new UnicodeRange(-1, 1); })).IsTrue();
        await Assert.That(ThrowsRange(() => { _ = new UnicodeRange(0x10000, 1); })).IsTrue();
        await Assert.That(ThrowsRange(() => { _ = new UnicodeRange(1, -1); })).IsTrue();
        await Assert.That(ThrowsRange(() => UnicodeRange.Create('z', 'a'))).IsTrue();
    }

    [Test]
    public async Task Utf16DestinationReportsShortBufferWithoutSplittingEscapes()
    {
        var source = "A<é😀";
        var destination = new char[2];

        var status = JavaScriptEncoder.Default.Encode(
            source.AsSpan(),
            destination,
            out var charsConsumed,
            out var charsWritten);

        await Assert.That(status).IsEqualTo(OperationStatus.DestinationTooSmall);
        await Assert.That(charsConsumed).IsEqualTo(1);
        await Assert.That(charsWritten).IsEqualTo(1);
        await Assert.That(new string(destination, 0, charsWritten)).IsEqualTo("A");

        destination = new char[7];
        status = JavaScriptEncoder.Default.Encode(
            source.AsSpan(),
            destination,
            out charsConsumed,
            out charsWritten);

        await Assert.That(status).IsEqualTo(OperationStatus.DestinationTooSmall);
        await Assert.That(charsConsumed).IsEqualTo(2);
        await Assert.That(charsWritten).IsEqualTo(7);
        await Assert.That(new string(destination, 0, charsWritten)).IsEqualTo("A\\u003C");
    }

    [Test]
    public async Task Utf8DestinationReportsShortBufferWithoutSplittingScalars()
    {
        var source = Encoding.UTF8.GetBytes("A<é😀");
        var destination = new byte[2];

        var status = JavaScriptEncoder.Default.EncodeUtf8(
            source,
            destination,
            out var bytesConsumed,
            out var bytesWritten);

        await Assert.That(status).IsEqualTo(OperationStatus.DestinationTooSmall);
        await Assert.That(bytesConsumed).IsEqualTo(1);
        await Assert.That(bytesWritten).IsEqualTo(1);
        await Assert.That(Encoding.UTF8.GetString(destination, 0, bytesWritten)).IsEqualTo("A");

        destination = new byte[7];
        status = JavaScriptEncoder.Default.EncodeUtf8(
            source,
            destination,
            out bytesConsumed,
            out bytesWritten);

        await Assert.That(status).IsEqualTo(OperationStatus.DestinationTooSmall);
        await Assert.That(bytesConsumed).IsEqualTo(2);
        await Assert.That(bytesWritten).IsEqualTo(7);
        await Assert.That(Encoding.UTF8.GetString(destination, 0, bytesWritten)).IsEqualTo("A\\u003C");
    }

    [Test]
    public async Task IncompleteSequencesRespectFinalBlockAndUseReplacementOnFinalInput()
    {
        var utf16Destination = new char[12];
        var utf16Status = JavaScriptEncoder.Default.Encode(
            "\uD83D".AsSpan(),
            utf16Destination,
            out var charsConsumed,
            out var charsWritten,
            isFinalBlock: false);

        await Assert.That(utf16Status).IsEqualTo(OperationStatus.NeedMoreData);
        await Assert.That(charsConsumed).IsEqualTo(0);
        await Assert.That(charsWritten).IsEqualTo(0);

        utf16Status = JavaScriptEncoder.Default.Encode(
            "\uD83D".AsSpan(),
            utf16Destination,
            out charsConsumed,
            out charsWritten);

        await Assert.That(utf16Status).IsEqualTo(OperationStatus.Done);
        await Assert.That(charsConsumed).IsEqualTo(1);
        await Assert.That(new string(utf16Destination, 0, charsWritten)).IsEqualTo("\\uFFFD");

        var utf8Source = new byte[] { 0xF0, 0x9F, 0x98 };
        var utf8Destination = new byte[12];
        var utf8Status = JavaScriptEncoder.Default.EncodeUtf8(
            utf8Source,
            utf8Destination,
            out var bytesConsumed,
            out var bytesWritten,
            isFinalBlock: false);

        await Assert.That(utf8Status).IsEqualTo(OperationStatus.NeedMoreData);
        await Assert.That(bytesConsumed).IsEqualTo(0);
        await Assert.That(bytesWritten).IsEqualTo(0);

        utf8Status = JavaScriptEncoder.Default.EncodeUtf8(
            utf8Source,
            utf8Destination,
            out bytesConsumed,
            out bytesWritten);

        await Assert.That(utf8Status).IsEqualTo(OperationStatus.Done);
        await Assert.That(bytesConsumed).IsEqualTo(3);
        await Assert.That(Encoding.UTF8.GetString(utf8Destination, 0, bytesWritten)).IsEqualTo("\\uFFFD");
    }

    [Test]
    public async Task InvalidSequencesEncodeReplacementScalarsAndContinue()
    {
        var invalidUtf16 = JavaScriptEncoder.Default.Encode("\uDC00(");
        var invalidUtf8 = JavaScriptEncoder.Default.EncodeUtf8(
            new byte[] { 0xC3, 0x28 },
            new byte[12],
            out var bytesConsumed,
            out var bytesWritten);

        await Assert.That(invalidUtf16).IsEqualTo("\\uFFFD(");
        await Assert.That(invalidUtf8).IsEqualTo(OperationStatus.Done);
        await Assert.That(bytesConsumed).IsEqualTo(2);
        await Assert.That(bytesWritten).IsEqualTo(7);
    }

    [Test]
    public async Task TextWriterAndSubstringOverloadsPreserveUnencodedSpans()
    {
        using var writer = new StringWriter();
        JavaScriptEncoder.Default.Encode(writer, "prefix<é>suffix", 6, 3);

        var characters = "prefix<é>suffix".ToCharArray();
        using var arrayWriter = new StringWriter();
        JavaScriptEncoder.Default.Encode(arrayWriter, characters, 6, 3);

        await Assert.That(writer.ToString()).IsEqualTo("\\u003C\\u00E9\\u003E");
        await Assert.That(arrayWriter.ToString()).IsEqualTo("\\u003C\\u00E9\\u003E");
    }

    [Test]
    public async Task HtmlAndUrlEscapingRemainOrdinalAndRoundTrip()
    {
        var html = WebUtility.HtmlEncode("<&>\"'\u00A9😀");
        var htmlDecoded = WebUtility.HtmlDecode(html);
        var url = WebUtility.UrlEncode("A B+é/😀");
        var urlDecoded = WebUtility.UrlDecode(url);

        await Assert.That(html).IsEqualTo("&lt;&amp;&gt;&quot;&#39;&#169;&#128512;");
        await Assert.That(htmlDecoded).IsEqualTo("<&>\"'\u00A9😀");
        await Assert.That(url).IsEqualTo("A+B%2B%C3%A9%2F%F0%9F%98%80");
        await Assert.That(urlDecoded).IsEqualTo("A B+é/😀");
    }

    [Test]
    public async Task RepeatedAndEquivalentEncodersProduceInvariantResults()
    {
        const string input = "a<é😀\\\"";
        var defaultEncoder = JavaScriptEncoder.Default;
        var cloneEncoder = JavaScriptEncoder.Create(new TextEncoderSettings(UnicodeRanges.BasicLatin));
        var first = defaultEncoder.Encode(input);
        var second = defaultEncoder.Encode(input);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).IsEqualTo(cloneEncoder.Encode(input));
        await Assert.That(defaultEncoder.EncodeUtf8(
            Encoding.UTF8.GetBytes(input),
            new byte[64],
            out var bytesConsumed,
            out var bytesWritten)).IsEqualTo(OperationStatus.Done);
        await Assert.That(bytesConsumed).IsEqualTo(Encoding.UTF8.GetByteCount(input));
        await Assert.That(bytesWritten).IsEqualTo(Encoding.UTF8.GetByteCount(first));
    }

    private static bool ThrowsRange(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }
}
