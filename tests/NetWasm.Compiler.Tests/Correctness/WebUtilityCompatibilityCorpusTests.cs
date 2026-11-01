namespace NetWasm.Compiler.Tests.Correctness;

public sealed class WebUtilityCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void WebUtilityMatchesDesktopAcrossProfilesAndTargets()
    {
        _ = CompilerCorrectnessEnvironment.Discover();
        runner.Run(new CorpusFixture(
            "WebUtilityCompatibilityCorpus",
            "NetWasm.Correctness.WebUtilityCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "WebUtility is a pure managed encoding contract; the same values and validation must hold on desktop and NetWasm.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        using System;
        using System.IO;
        using System.Net;

        namespace NetWasm.Correctness.WebUtilityCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => HtmlEncoding(),
                1 => LongDecimalEntity(),
                2 => HexParse(),
                3 => SupplementaryHexEntity(),
                4 => SignedDecimalEntity(),
                5 => InvalidNumericEntities(),
                6 => NamedEntities(),
                7 => WriterOverloads(),
                8 => UrlStrings(),
                9 => UrlByteRanges(),
                10 => NullByteRanges(),
                11 => RangeValidation(),
                12 => MalformedInput(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int HtmlEncoding()
            {
                string value = "<&>\"'\u00a0\ud83d\ude00\ud800";
                return Result(
                    WebUtility.HtmlEncode(null) is null &&
                    WebUtility.HtmlEncode(value) == "&lt;&amp;&gt;&quot;&#39;&#160;&#128512;\ufffd",
                    100);
            }

            private static int LongDecimalEntity() => Result(
                WebUtility.HtmlDecode("&#0000000000065;") == "A", 101);

            private static int HexParse() => Result(
                uint.TryParse("1F600".AsSpan(), System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out uint value) && value == 0x1F600,
                102);

            private static int SupplementaryHexEntity() => Result(
                WebUtility.HtmlDecode("&#x1F600;") == "\ud83d\ude00", 102);

            private static int SignedDecimalEntity() => Result(
                WebUtility.HtmlDecode("&#  +66  ;") == "B", 103);

            private static int InvalidNumericEntities() => Result(
                WebUtility.HtmlDecode("&#xD800;&#1114112;") == "&#xD800;&#1114112;", 104);

            private static int NamedEntities() => Result(
                WebUtility.HtmlDecode("&copy;&thetasym;&alefsym;&unknown;") == "\u00a9\u03d1\u2135&unknown;",
                105);

            private static int WriterOverloads()
            {
                var encoded = new StringWriter();
                WebUtility.HtmlEncode("<&", encoded);
                var decoded = new StringWriter();
                WebUtility.HtmlDecode("&lt;&amp;", decoded);

                bool encodeNullWriter = false;
                bool decodeNullWriter = false;
                try { WebUtility.HtmlEncode(null, null!); }
                catch (ArgumentNullException) { encodeNullWriter = true; }
                try { WebUtility.HtmlDecode(null, null!); }
                catch (ArgumentNullException) { decodeNullWriter = true; }

                return Result(
                    encoded.ToString() == "&lt;&amp;" &&
                    decoded.ToString() == "<&" &&
                    encodeNullWriter &&
                    decodeNullWriter,
                    106);
            }

            private static int UrlStrings() => Result(
                WebUtility.UrlEncode(null) is null &&
                WebUtility.UrlDecode(null) is null &&
                WebUtility.UrlEncode("a b/\u00e9") == "a+b%2F%C3%A9" &&
                WebUtility.UrlDecode("a+b%2F%C3%A9") == "a b/\u00e9",
                107);

            private static int UrlByteRanges()
            {
                byte[] encoded = WebUtility.UrlEncodeToBytes([0, 32, 255], 1, 2)!;
                byte[] decoded = WebUtility.UrlDecodeToBytes(encoded, 0, encoded.Length)!;
                return Result(
                    encoded.Length == 4 && encoded[0] == (byte)'+' &&
                    encoded[1] == (byte)'%' && encoded[2] == (byte)'F' && encoded[3] == (byte)'F' &&
                    decoded.Length == 2 && decoded[0] == 32 && decoded[1] == 255,
                    108);
            }

            private static int NullByteRanges() => Result(
                WebUtility.UrlEncodeToBytes(null, int.MaxValue, 0) is null &&
                WebUtility.UrlDecodeToBytes(null, int.MaxValue, 0) is null,
                109);

            private static int RangeValidation()
            {
                bool nullEncode = false;
                bool nullDecode = false;
                bool offset = false;
                bool count = false;
                try { WebUtility.UrlEncodeToBytes(null, 0, 1); }
                catch (ArgumentNullException) { nullEncode = true; }
                try { WebUtility.UrlDecodeToBytes(null, 0, 1); }
                catch (ArgumentNullException) { nullDecode = true; }
                try { WebUtility.UrlEncodeToBytes([], 1, 0); }
                catch (ArgumentOutOfRangeException) { offset = true; }
                try { WebUtility.UrlDecodeToBytes([], 0, 1); }
                catch (ArgumentOutOfRangeException) { count = true; }
                return Result(nullEncode && nullDecode && offset && count, 110);
            }

            private static int MalformedInput() => Result(
                WebUtility.UrlDecode("%ZZ+%2") == "%ZZ %2" &&
                WebUtility.UrlDecode("%FF") == "\ufffd" &&
                WebUtility.HtmlDecode("&amp&copy;") == "&amp\u00a9",
                111);

            private static int Result(bool passed, int value) => passed ? value : -value;
        }
        """;
}
