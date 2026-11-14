#if NETWASM
#if COVERAGE
extern alias coverage;
using PortAllowedBmpCodePointsBitmap = coverage::System.Text.Encodings.Web.AllowedBmpCodePointsBitmap;
using PortEncoderDebug = coverage::System.Text.Encodings.Web.Debug;
using PortEncoderHexConverter = coverage::System.Text.Encodings.Web.EncoderHexConverter;
using PortJavaScriptEncoder = coverage::System.Text.Encodings.Web.JavaScriptEncoder;
using PortOptimizedInboxTextEncoder = coverage::System.Text.Encodings.Web.OptimizedInboxTextEncoder;
using PortScalarEscaperBase = coverage::System.Text.Encodings.Web.ScalarEscaperBase;
using PortTextEncoder = coverage::System.Text.Encodings.Web.TextEncoder;
using PortUnicodeRanges = coverage::System.Text.Unicode.UnicodeRanges;
#else
using PortAllowedBmpCodePointsBitmap = System.Text.Encodings.Web.AllowedBmpCodePointsBitmap;
using PortEncoderDebug = System.Text.Encodings.Web.Debug;
using PortEncoderHexConverter = System.Text.Encodings.Web.EncoderHexConverter;
using PortJavaScriptEncoder = System.Text.Encodings.Web.JavaScriptEncoder;
using PortOptimizedInboxTextEncoder = System.Text.Encodings.Web.OptimizedInboxTextEncoder;
using PortScalarEscaperBase = System.Text.Encodings.Web.ScalarEscaperBase;
using PortTextEncoder = System.Text.Encodings.Web.TextEncoder;
using PortUnicodeRanges = System.Text.Unicode.UnicodeRanges;
#endif
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.Unicode;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Encodings.Web.Tests;

public sealed class UnicodeRuntimePrimitiveTests
{
    [Test]
    public async Task RuntimeUnicodePrimitivesCoverAssertionsAndScalarBoundaries()
    {
        Debug.Assert(true);
        Debug.Assert(false, "false assertions remain non-throwing in the target runtime");

        UnicodeDebug.AssertIsBmpCodePoint(0);
        UnicodeDebug.AssertIsBmpCodePoint(char.MaxValue);
        UnicodeDebug.AssertIsBmpCodePoint(char.MaxValue + 1u);
        UnicodeDebug.AssertIsValidScalar(0);
        UnicodeDebug.AssertIsValidScalar(0x10FFFF);
        UnicodeDebug.AssertIsValidScalar(0x110000);
        UnicodeDebug.AssertIsValidSupplementaryPlaneScalar(0x10000);
        UnicodeDebug.AssertIsValidSupplementaryPlaneScalar(0x10FFFF);
        UnicodeDebug.AssertIsValidSupplementaryPlaneScalar(char.MaxValue);

        await Assert.That(UnicodeUtility.IsBmpCodePoint(0)).IsTrue();
        await Assert.That(UnicodeUtility.IsBmpCodePoint(char.MaxValue)).IsTrue();
        await Assert.That(UnicodeUtility.IsBmpCodePoint(char.MaxValue + 1u)).IsFalse();
        await Assert.That(UnicodeUtility.IsAsciiCodePoint(0)).IsTrue();
        await Assert.That(UnicodeUtility.IsAsciiCodePoint(0x7F)).IsTrue();
        await Assert.That(UnicodeUtility.IsAsciiCodePoint(0x80)).IsFalse();
    }

    [Test]
    public async Task AdaptedEncodingMembersCoverPortBoundaries()
    {
        PortEncoderDebug.Assert(true);
        PortEncoderDebug.Assert(false, "false assertions remain non-throwing in the target runtime");

        var byteBuffer = new byte[2];
        var charBuffer = new char[2];
        PortEncoderHexConverter.ToBytesBuffer(0xAF, byteBuffer);
        PortEncoderHexConverter.ToCharsBuffer(0xAF, charBuffer);
        await Assert.That(byteBuffer[0]).IsEqualTo((byte)'A');
        await Assert.That(byteBuffer[1]).IsEqualTo((byte)'F');
        await Assert.That(new string(charBuffer)).IsEqualTo("AF");

        var encoded = PortJavaScriptEncoder.Default.Encode("A<é😀");
        await Assert.That(encoded).IsEqualTo("A\\u003C\\u00E9\\uD83D\\uDE00");
        using var writer = new StringWriter();
        PortJavaScriptEncoder.Default.Encode(writer, "A<é", 1, 2);
        await Assert.That(writer.ToString()).IsEqualTo("\\u003C\\u00E9");
        using var plainWriter = new StringWriter();
        PortJavaScriptEncoder.Default.Encode(plainWriter, "plain", 0, 5);
        await Assert.That(plainWriter.ToString()).IsEqualTo("plain");

        var nullOutputThrows = false;
        try { PortJavaScriptEncoder.Default.Encode(null!, "plain", 0, 5); }
        catch (ArgumentNullException) { nullOutputThrows = true; }
        var nullValueThrows = false;
        try { PortJavaScriptEncoder.Default.Encode(new StringWriter(), (string)null!, 0, 0); }
        catch (ArgumentNullException) { nullValueThrows = true; }
        var invalidRangeThrows = false;
        try { PortJavaScriptEncoder.Default.Encode(new StringWriter(), "plain", -1, 1); }
        catch (ArgumentOutOfRangeException) { invalidRangeThrows = true; }
        var invalidCountThrows = false;
        try { PortJavaScriptEncoder.Default.Encode(new StringWriter(), "plain", 0, 6); }
        catch (ArgumentOutOfRangeException) { invalidCountThrows = true; }
        await Assert.That(nullOutputThrows && nullValueThrows && invalidRangeThrows && invalidCountThrows).IsTrue();

        await Assert.That(PortUnicodeRanges.None.Length).IsEqualTo(0);
        await Assert.That(PortUnicodeRanges.BasicLatin.Length).IsEqualTo(128);
        await Assert.That(PortUnicodeRanges.All.Length).IsEqualTo(65536);

        var bitmap = default(PortAllowedBmpCodePointsBitmap);
        _ = new PortOptimizedInboxTextEncoder(new EmptyEscaper(), in bitmap);
        var nullEscaperThrows = false;
        try { _ = new PortOptimizedInboxTextEncoder(null!, in bitmap); }
        catch (ArgumentNullException) { nullEscaperThrows = true; }
        await Assert.That(nullEscaperThrows).IsTrue();

        var invalid = new InvalidProgressEncoder();
        var invalidProgressThrows = false;
        try { _ = invalid.Encode("<"); }
        catch (ArgumentException) { invalidProgressThrows = true; }
        await Assert.That(invalidProgressThrows).IsTrue();
    }

    private sealed class EmptyEscaper : PortScalarEscaperBase
    {
        internal override int EncodeUtf16(Rune value, Span<char> destination) => 0;
        internal override int EncodeUtf8(Rune value, Span<byte> destination) => 0;
    }

    private sealed class InvalidProgressEncoder : PortTextEncoder
    {
        public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
        {
            numberOfCharactersWritten = 0;
            return true;
        }

        public override unsafe int FindFirstCharacterToEncode(char* text, int textLength) => 0;
        public override bool WillEncode(int unicodeScalar) => true;
        public override int MaxOutputCharactersPerInputCharacter => 1;

        private protected override OperationStatus EncodeCore(
            ReadOnlySpan<char> source,
            Span<char> destination,
            out int charsConsumed,
            out int charsWritten,
            bool isFinalBlock)
        {
            charsConsumed = 0;
            charsWritten = 0;
            return OperationStatus.Done;
        }
    }
}
#endif
