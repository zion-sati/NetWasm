using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class CorrectnessTestAssets
{
    public static CorpusHostIdentity HostIdentity { get; } = new("test-framework", "test-runtime", "test-description", "test-architecture");

    public static CorpusFixture CreateFixture(string name = "HarnessUnit") => new(
        name,
        "NetWasm.Correctness.HarnessUnit",
        """
        namespace NetWasm.Correctness.HarnessUnit;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Run(int input)
            {
                _trace = input * 2;
                if (input < 0) throw new System.ArgumentOutOfRangeException();
                return input + 1;
            }

            public static int Trace() => _trace;
        }
        """,
        [3, -1]);

    public static CorpusFixture CreateUriFixture() => new CorpusFixture(
        "SystemUriCompatibility",
        "NetWasm.Correctness.SystemUriCompatibility",
        """
        namespace NetWasm.Correctness.SystemUriCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                return input switch
                {
                    0 => RelativeConstruction(),
                    1 => AbsoluteConstruction(),
                    2 => EqualityAndHashCode(),
                    3 => RejectRelativeAsAbsolute(),
                    4 => RejectAbsoluteAsRelative(),
                    5 => DirectConstructionAndNullHandling(),
                    6 => RejectInvalidKind(),
                    7 => RejectMalformedAbsoluteUri(),
                    8 => CredentialResourceCarrierContract(),
                    _ => -1,
                };
            }

            private static int RelativeConstruction()
            {
                var uri = new System.Uri("relative/path", System.UriKind.Relative);
                return !uri.IsAbsoluteUri &&
                    uri.OriginalString == "relative/path" &&
                    uri.ToString() == "relative/path"
                    ? 101
                    : 1;
            }

            private static int DirectConstructionAndNullHandling()
            {
                var uri = new System.Uri("https://example.test/direct");
                if (!uri.IsAbsoluteUri ||
                    uri.OriginalString != "https://example.test/direct" ||
                    uri.ToString() != "https://example.test/direct")
                {
                    return 1;
                }

                try
                {
                    _ = new System.Uri((string)null!);
                    return 2;
                }
                catch (System.ArgumentNullException)
                {
                    return 106;
                }
                catch (System.UriFormatException)
                {
                    return 106;
                }
            }

            private static int AbsoluteConstruction()
            {
                var uri = new System.Uri(
                    "https://example.test/path?x=1#frag",
                    System.UriKind.Absolute);
                return uri.IsAbsoluteUri &&
                    uri.Scheme == "https" &&
                    uri.Host == "example.test" &&
                    uri.PathAndQuery == "/path?x=1" &&
                    uri.Fragment == "#frag" &&
                    uri.OriginalString == uri.ToString()
                    ? 102
                    : 1;
            }

            private static int EqualityAndHashCode()
            {
                var left = new System.Uri("https://example.test/path", System.UriKind.Absolute);
                var equivalent = new System.Uri(
                    "https://example.test/path",
                    System.UriKind.Absolute);
                var distinct = new System.Uri(
                    "https://example.test/other",
                    System.UriKind.Absolute);
                System.Uri? nullUri = null;
                object boxedEquivalent = equivalent;
                return left.Equals(equivalent) &&
                    equivalent.Equals(left) &&
                    left.Equals((object)equivalent) &&
                    !left.Equals(distinct) &&
                    !left.Equals((object)distinct) &&
                    !left.Equals(nullUri) &&
                    left == equivalent &&
                    !(left != equivalent) &&
                    left != distinct &&
                    !(left == distinct) &&
                    left != nullUri &&
                    nullUri != left &&
                    nullUri == null &&
                    !(nullUri != null) &&
                    left.Equals(boxedEquivalent) &&
                    left.GetHashCode() == equivalent.GetHashCode()
                    ? 103
                    : 1;
            }

            private static int RejectRelativeAsAbsolute()
            {
                try
                {
                    _ = new System.Uri("relative/path", System.UriKind.Absolute);
                    return 1;
                }
                catch (System.UriFormatException)
                {
                    return 104;
                }
            }

            private static int RejectAbsoluteAsRelative()
            {
                try
                {
                    _ = new System.Uri("https://example.test/path", System.UriKind.Relative);
                    return 1;
                }
                catch (System.UriFormatException)
                {
                    return 105;
                }
            }

            private static int RejectInvalidKind()
            {
                try
                {
                    _ = new System.Uri("relative/path", (System.UriKind)7);
                    return 1;
                }
                catch (System.ArgumentException)
                {
                    return 107;
                }
            }

            private static int RejectMalformedAbsoluteUri()
            {
                try
                {
                    _ = new System.Uri("https://[invalid");
                    return 1;
                }
                catch (System.UriFormatException)
                {
                    return 108;
                }
            }

            private static int CredentialResourceCarrierContract()
            {
                var carrier = new CredentialResourceCarrier(
                    new System.Uri("https://example.test/resource", System.UriKind.Absolute));
                var returned = carrier.Select(carrier.ResourceUri);
                return System.Object.ReferenceEquals(carrier.ResourceUri, returned) &&
                    returned.AbsoluteUri == "https://example.test/resource"
                    ? 109
                    : 1;
            }

            private sealed class CredentialResourceCarrier
            {
                private readonly System.Uri _credentialUri;

                public CredentialResourceCarrier(System.Uri credentialUri)
                {
                    _credentialUri = credentialUri;
                }

                public System.Uri ResourceUri => _credentialUri;

                public System.Uri Select(System.Uri candidate) => candidate;
            }
        }
        """,
        [0, 1, 2, 3, 4, 5, 6, 7, 8]) with
    {
        ExposesLegacyTrace = false,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        FrozenOracleEvidencePath =
            "compiler-qualification/diagnostics/system-uri-oracle.json",
        SameSourceReason = "The fixture exercises portable System.Uri CoreLib behavior."
    };

    public static CorpusFixture CreateUriU01BFixture() => new CorpusFixture(
        "SystemUriU01BCompatibility",
        "NetWasm.Correctness.SystemUriU01BCompatibility",
        """
        namespace NetWasm.Correctness.SystemUriU01BCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => AbsoluteComponents(),
                1 => DefaultPortAndCanonicalPath(),
                2 => RelativePreservesText(),
                3 => RelativeResolution(),
                4 => QueryAndFragmentResolution(),
                5 => AuthorityAndUserInfo(),
                6 => MailtoOpaqueAuthority(),
                7 => RejectMalformedPort(),
                8 => SchemeCaseAndHostCase(),
                9 => DotSegmentResolution(),
                _ => -1,
            };

            private static int AbsoluteComponents()
            {
                var uri = new System.Uri(
                    "https://user:pass@Example.test:8443/a/b?x=1#f",
                    System.UriKind.Absolute);
                return uri.Scheme == "https" &&
                    uri.Authority == "example.test:8443" &&
                    uri.UserInfo == "user:pass" &&
                    uri.Host == "example.test" &&
                    uri.AbsolutePath == "/a/b" &&
                    uri.PathAndQuery == "/a/b?x=1" &&
                    uri.Query == "?x=1" &&
                    uri.Fragment == "#f" &&
                    uri.Port == 8443 &&
                    uri.ToString() == "https://user:pass@example.test:8443/a/b?x=1#f"
                    ? 201 : 1;
            }

            private static int DefaultPortAndCanonicalPath()
            {
                var uri = new System.Uri("https://Example.test/a/../b", System.UriKind.Absolute);
                return uri.AbsolutePath == "/b" &&
                    uri.PathAndQuery == "/b" &&
                    uri.Authority == "example.test" &&
                    uri.Port == 443 &&
                    uri.ToString() == "https://example.test/b"
                    ? 202 : 1;
            }

            private static int RelativePreservesText()
            {
                var uri = new System.Uri("a/../b?x=1#f", System.UriKind.Relative);
                return !uri.IsAbsoluteUri && uri.ToString() == "a/../b?x=1#f" &&
                    uri.OriginalString == "a/../b?x=1#f"
                    ? 203 : 1;
            }

            private static int RelativeResolution()
            {
                var baseUri = new System.Uri("https://example.test/a/b/c", System.UriKind.Absolute);
                var resolved = new System.Uri(baseUri, "../d");
                return resolved.ToString() == "https://example.test/a/d" ? 204 : 1;
            }

            private static int QueryAndFragmentResolution()
            {
                var baseUri = new System.Uri("https://example.test/a/b/c?old=1#old");
                var query = new System.Uri(baseUri, "?new=2");
                var fragment = new System.Uri(baseUri, "#new");
                return query.ToString() == "https://example.test/a/b/c?new=2" &&
                    fragment.ToString() == "https://example.test/a/b/c?old=1#new"
                    ? 205 : 1;
            }

            private static int AuthorityAndUserInfo()
            {
                var uri = new System.Uri("http://user@example.test:8080/path", System.UriKind.Absolute);
                return uri.Authority == "example.test:8080" &&
                    uri.UserInfo == "user" && uri.Host == "example.test"
                    ? 206 : 1;
            }

            private static int MailtoOpaqueAuthority()
            {
                var uri = new System.Uri("mailto:user@example.test", System.UriKind.Absolute);
                return uri.Scheme == "mailto" && uri.Host == "example.test" &&
                    uri.UserInfo == "user" && uri.AbsolutePath == string.Empty &&
                    uri.ToString() == "mailto:user@example.test"
                    ? 207 : 1;
            }

            private static int RejectMalformedPort()
            {
                try
                {
                    _ = new System.Uri("https://example.test:service/path", System.UriKind.Absolute);
                    return 1;
                }
                catch (System.UriFormatException)
                {
                    return 208;
                }
            }

            private static int SchemeCaseAndHostCase()
            {
                var uri = new System.Uri("HTTP://EXAMPLE.TEST/path", System.UriKind.Absolute);
                return uri.Scheme == "http" && uri.Host == "example.test" &&
                    uri.ToString() == "http://example.test/path"
                    ? 209 : 1;
            }

            private static int DotSegmentResolution()
            {
                var baseUri = new System.Uri("https://example.test/a/b/c/", System.UriKind.Absolute);
                var resolved = new System.Uri(baseUri, "./d/../e");
                return resolved.AbsolutePath == "/a/b/c/e" &&
                    resolved.ToString() == "https://example.test/a/b/c/e"
                    ? 210 : 1;
            }
        }
        """,
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]) with
    {
        ExposesLegacyTrace = false,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        FrozenOracleEvidencePath =
            "compiler-qualification/diagnostics/system-uri-u01b-oracle.json",
        SameSourceReason = "The fixture exercises U01B portable parsing and relative resolution."
    };

    public static CorpusFixture CreateUriU01CFixture() => new CorpusFixture(
        "SystemUriU01CCompatibility",
        "NetWasm.Correctness.SystemUriU01CCompatibility",
        """
        namespace NetWasm.Correctness.SystemUriU01CCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => EscapeAndUnescape(),
                1 => SpanEscape(),
                2 => ComponentCompositions(),
                3 => LeftParts(),
                4 => WellFormedAndTryCreate(),
                5 => HexHelpers(),
                6 => EscapedComponentBoundaries(),
                _ => -1,
            };

            private static int EscapeAndUnescape()
            {
                return System.Uri.EscapeDataString("a b/c?d") == "a%20b%2Fc%3Fd" &&
                    System.Uri.EscapeDataString("café") == "caf%C3%A9" &&
                    System.Uri.UnescapeDataString("a%20b%2Fc%3Fd") == "a b/c?d" &&
                    System.Uri.UnescapeDataString("%E2%82%AC") == "€" &&
                    System.Uri.UnescapeDataString("%zz") == "%zz"
                    ? 301 : 1;
            }

            private static int SpanEscape()
            {
                var source = new System.ReadOnlySpan<char>(new[] { 'a', ' ', 'b' });
                var escaped = System.Uri.EscapeDataString(source);
                return escaped == "a%20b" &&
                    System.Uri.UnescapeDataString(
                        new System.ReadOnlySpan<char>(new[] { 'a', '%', '2', '0', 'b' })) == "a b"
                    ? 302 : 1;
            }

            private static int ComponentCompositions()
            {
                var uri = new System.Uri(
                    "https://user:pass@example.test:8443/a b?x=1#f",
                    System.UriKind.Absolute);
                return uri.GetComponents(System.UriComponents.SchemeAndServer, System.UriFormat.UriEscaped) ==
                        "https://example.test:8443" &&
                    uri.GetComponents(
                        System.UriComponents.UserInfo | System.UriComponents.Host | System.UriComponents.Port,
                        System.UriFormat.UriEscaped) == "user:pass@example.test:8443" &&
                    uri.GetComponents(System.UriComponents.PathAndQuery, System.UriFormat.UriEscaped) ==
                        "/a%20b?x=1" &&
                    uri.GetComponents(System.UriComponents.AbsoluteUri, System.UriFormat.Unescaped) ==
                        "https://user:pass@example.test:8443/a b?x=1#f"
                    ? 303 : 1;
            }

            private static int LeftParts()
            {
                var uri = new System.Uri(
                    "https://user:pass@example.test:8443/a b?x=1#f",
                    System.UriKind.Absolute);
                return uri.GetLeftPart(System.UriPartial.Scheme) == "https://" &&
                    uri.GetLeftPart(System.UriPartial.Authority) ==
                        "https://user:pass@example.test:8443" &&
                    uri.GetLeftPart(System.UriPartial.Path) ==
                        "https://user:pass@example.test:8443/a%20b" &&
                    uri.GetLeftPart(System.UriPartial.Query) ==
                        "https://user:pass@example.test:8443/a%20b?x=1"
                    ? 304 : 1;
            }

            private static int WellFormedAndTryCreate()
            {
                var good = new System.Uri("https://example.test/a%20b", System.UriKind.Absolute);
                var bad = new System.Uri("https://example.test/a b", System.UriKind.Absolute);
                var tryGood = System.Uri.TryCreate(
                    "https://example.test/a%20b",
                    System.UriKind.Absolute,
                    out var created);
                var tryBad = System.Uri.TryCreate(
                    "https://example.test:service",
                    System.UriKind.Absolute,
                    out var rejected);
                return good.IsWellFormedOriginalString() && !bad.IsWellFormedOriginalString() &&
                    tryGood && created is not null && !tryBad && rejected is null &&
                    System.Uri.IsWellFormedUriString("https://example.test/a%20b", System.UriKind.Absolute) &&
                    !System.Uri.IsWellFormedUriString("https://example.test/a b", System.UriKind.Absolute)
                    ? 305 : 1;
            }

            private static int HexHelpers()
            {
                return System.Uri.IsHexDigit('A') && System.Uri.IsHexDigit('f') &&
                    System.Uri.IsHexDigit('9') && !System.Uri.IsHexDigit('g') &&
                    System.Uri.FromHex('A') == 10 && System.Uri.FromHex('f') == 15 &&
                    System.Uri.FromHex('9') == 9
                    ? 306 : 1;
            }

            private static int EscapedComponentBoundaries()
            {
                var uri = new System.Uri("https://example.test/a%2Fb?x=%2F#%66", System.UriKind.Absolute);
                return uri.GetComponents(System.UriComponents.Path, System.UriFormat.UriEscaped) == "a%2Fb" &&
                    uri.GetComponents(System.UriComponents.Path, System.UriFormat.Unescaped) == "a/b" &&
                    uri.GetComponents(System.UriComponents.Query, System.UriFormat.Unescaped) == "x=/" &&
                    uri.GetComponents(
                        System.UriComponents.Fragment | System.UriComponents.KeepDelimiter,
                        System.UriFormat.Unescaped) == "#f"
                    ? 307 : 1;
            }
        }
        """,
        [0, 1, 2, 3, 4, 5, 6]) with
    {
        ExposesLegacyTrace = false,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        FrozenOracleEvidencePath =
            "compiler-qualification/diagnostics/system-uri-u01c-oracle.json",
        SameSourceReason = "The fixture exercises U01C escaping, components, and well-formedness."
    };

    public static CorpusFixture CreateUriU02AFixture() => new CorpusFixture(
        "SystemUriU02ACompatibility",
        "NetWasm.Correctness.SystemUriU02ACompatibility",
        """
        namespace NetWasm.Correctness.SystemUriU02ACompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => HostClassification(),
                1 => HostProperties(),
                2 => ParserRegistry(),
                3 => BuilderComponents(),
                4 => BuilderMaterialization(),
                5 => ParserOptions(),
                _ => -1,
            };

            private static int HostClassification()
            {
                return System.Uri.CheckHostName("1.2.3.4") == System.UriHostNameType.IPv4 &&
                    System.Uri.CheckHostName("1.2.3") == System.UriHostNameType.IPv4 &&
                    System.Uri.CheckHostName("[::1]") == System.UriHostNameType.IPv6 &&
                    System.Uri.CheckHostName("::1") == System.UriHostNameType.IPv6 &&
                    System.Uri.CheckHostName("example.test") == System.UriHostNameType.Dns &&
                    System.Uri.CheckHostName("bad host") == System.UriHostNameType.Unknown
                    ? 401 : 1;
            }

            private static int HostProperties()
            {
                var file = new System.Uri("file://server/share/a", System.UriKind.Absolute);
                var ipv6 = new System.Uri("https://[::1]:443/a", System.UriKind.Absolute);
                return file.IsFile && file.IsUnc && file.HostNameType == System.UriHostNameType.Dns &&
                    file.DnsSafeHost == "server" &&
                    ipv6.Host == "[::1]" && ipv6.DnsSafeHost == "::1" &&
                    ipv6.IsDefaultPort && ipv6.Authority == "[::1]"
                    ? 402 : 1;
            }

            private static int ParserRegistry()
            {
                var parser = new System.GenericUriParser(
                    System.GenericUriParserOptions.GenericAuthority |
                    System.GenericUriParserOptions.AllowEmptyAuthority);
                System.UriParser.Register(parser, "u02a-custom", 1234);
                return System.UriParser.IsKnownScheme("http") &&
                    System.UriParser.IsKnownScheme("u02a-custom") &&
                    !System.UriParser.IsKnownScheme("u02a-unknown")
                    ? 403 : 1;
            }

            private static int BuilderComponents()
            {
                var builder = new System.UriBuilder(
                    "https", "example.test", 443, "/a b", "?x=1#f");
                builder.UserName = "u";
                builder.Password = "p";
                return builder.Scheme == "https" && builder.Host == "example.test" &&
                    builder.Port == 443 && builder.Path == "/a%20b" &&
                    builder.Query == "?x=1" && builder.Fragment == "#f" &&
                    builder.UserName == "u" && builder.Password == "p"
                    ? 404 : 1;
            }

            private static int BuilderMaterialization()
            {
                var builder = new System.UriBuilder("https://example.test/a?q=1#f");
                var uri = builder.Uri;
                return uri.IsAbsoluteUri && uri.Host == "example.test" &&
                    uri.AbsolutePath == "/a" && uri.Query == "?q=1" &&
                    uri.Fragment == "#f" && builder.ToString() ==
                    "https://example.test:443/a?q=1#f"
                    ? 405 : 1;
            }

            private static int ParserOptions()
            {
                var options = System.GenericUriParserOptions.Idn |
                    System.GenericUriParserOptions.IriParsing |
                    System.GenericUriParserOptions.DontCompressPath;
                return (int)options == 1664 ? 406 : 1;
            }
        }
        """,
        [0, 1, 2, 3, 4, 5]) with
    {
        ExposesLegacyTrace = false,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        FrozenOracleEvidencePath =
            "compiler-qualification/diagnostics/system-uri-u02a-oracle.json"
    };

    public static CorpusFixture CreateUriU02BFixture() => new CorpusFixture(
        "SystemUriU02BCompatibility",
        "NetWasm.Correctness.SystemUriU02BCompatibility",
        """
        #pragma warning disable CS0618
        namespace NetWasm.Correctness.SystemUriU02BCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => GermanIdnHost(),
                1 => JapaneseIriPath(),
                2 => UnicodeHostRoundTrip(),
                3 => EscapedConstructorForms(),
                4 => TryCreateOverloads(),
                5 => ComponentAudit(),
                6 => ConstantsAndBoundaries(),
                _ => -1,
            };

            private static int GermanIdnHost()
            {
                var uri = new System.Uri(
                    "https://bücher.example/straße?q=é",
                    System.UriKind.Absolute);
                return uri.Host == "bücher.example" &&
                    uri.DnsSafeHost == "bücher.example" &&
                    uri.IdnHost == "xn--bcher-kva.example" &&
                    uri.AbsoluteUri == "https://bücher.example/stra%C3%9Fe?q=%C3%A9"
                    ? 501 : 1;
            }

            private static int JapaneseIriPath()
            {
                var uri = new System.Uri(
                    "https://例え.テスト/パス",
                    System.UriKind.Absolute);
                return uri.Host == "例え.テスト" &&
                    uri.IdnHost == "xn--r8jz45g.xn--zckzah" &&
                    uri.AbsolutePath == "/%E3%83%91%E3%82%B9" &&
                    uri.GetComponents(
                    System.UriComponents.AbsoluteUri,
                    System.UriFormat.UriEscaped) ==
                    "https://例え.テスト/%E3%83%91%E3%82%B9"
                    ? 502 : 1;
            }

            private static int UnicodeHostRoundTrip()
            {
                var uri = new System.Uri(
                    "https://☃.example/",
                    System.UriKind.Absolute);
                return uri.ToString() == "https://☃.example/" &&
                    uri.IdnHost == "xn--n3h.example" &&
                    uri.GetComponents(
                        System.UriComponents.Host,
                        System.UriFormat.UriEscaped) == "☃.example"
                    ? 503 : 1;
            }

            private static int EscapedConstructorForms()
            {
                var baseUri = new System.Uri(
                    "https://example.test/root/",
                    System.UriKind.Absolute);
                var direct = new System.Uri("https://example.test/a%20b", true);
                var combined = new System.Uri(baseUri, "child%20item", true);
                return direct.UserEscaped && combined.UserEscaped &&
                    direct.AbsoluteUri == "https://example.test/a%20b" &&
                    combined.AbsoluteUri == "https://example.test/root/child%20item"
                    ? 504 : 1;
            }

            private static int TryCreateOverloads()
            {
                var baseUri = new System.Uri("https://example.test/root", System.UriKind.Absolute);
                var one = System.Uri.TryCreate(baseUri, "child", out var first);
                var two = System.Uri.TryCreate(
                    "https://example.test/child",
                    new System.UriCreationOptions(),
                    out var second);
                var three = System.Uri.TryCreate(baseUri, first, out var third);
                return one && first is not null && two && second is not null &&
                    three && third is not null && third.IsAbsoluteUri
                    ? 505 : 1;
            }

            private static int ComponentAudit()
            {
                var uri = new System.Uri(
                    "https://bücher.example/straße?q=é#終",
                    System.UriKind.Absolute);
                var escaped = uri.GetComponents(
                    System.UriComponents.PathAndQuery,
                    System.UriFormat.UriEscaped);
                var unescaped = uri.GetComponents(
                    System.UriComponents.PathAndQuery,
                    System.UriFormat.Unescaped);
                return escaped == "/stra%C3%9Fe?q=%C3%A9" &&
                    unescaped == "/straße?q=é" &&
                    uri.Query == "?q=%C3%A9" &&
                    uri.Fragment == "#%E7%B5%82" &&
                    uri.GetComponents(
                        System.UriComponents.NormalizedHost,
                        System.UriFormat.UriEscaped) == "bücher.example" &&
                    uri.AbsoluteUri.EndsWith("#%E7%B5%82", System.StringComparison.Ordinal)
                    ? 506 : 1;
            }

            private static int ConstantsAndBoundaries()
            {
                return System.Uri.UriSchemeHttp == "http" &&
                    System.Uri.UriSchemeHttps == "https" &&
                    System.Uri.UriSchemeWs == "ws" &&
                    System.Uri.UriSchemeWss == "wss" &&
                    System.Uri.CheckSchemeName("custom") &&
                    !System.Uri.CheckSchemeName("bad scheme")
                    ? 507 : 1;
            }
        }
        """,
        [0, 1, 2, 3, 4, 5, 6]) with
    {
        ExposesLegacyTrace = false,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
        FrozenOracleEvidencePath =
            "compiler-qualification/diagnostics/system-uri-u02b-oracle.json"
    };

    public static ServiceProvider CreateServices(string? repositoryStartDirectory = null) => new ServiceCollection()
        .AddCompilerCorrectnessHarness(repositoryStartDirectory)
        .BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

    public static IOracleModePolicyRegistry CreateOracleModes() =>
        new OracleModePolicyRegistry(
        [
            new SameIlOracleModePolicy(),
            new SameSourceOracleModePolicy(),
            new FrozenDesktopOracleModePolicy(),
        ]);

    public static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "netwasm-correctness-unit",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
