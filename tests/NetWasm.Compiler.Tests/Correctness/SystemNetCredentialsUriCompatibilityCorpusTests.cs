namespace NetWasm.Compiler.Tests.Correctness;

// This is an end-to-end differential acceptance fixture, not a unit test.
public sealed class SystemNetCredentialsUriCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CredentialsAndUriSemanticsMatchDesktopAcrossProfilesAndTargets()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        runner.Run(new CorpusFixture(
            "SystemNetCredentialsUriCompatibilityCorpus",
            "NetWasm.Correctness.SystemNetCredentialsUri",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The XML resolver credential contract is a pure data carrier; " +
                "the same Uri and credential semantics must hold on desktop and NetWasm.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        using System;
        using System.Net;

        namespace NetWasm.Correctness.SystemNetCredentialsUri;

        public static class EntryPoint
        {
            private static readonly ICredentials FieldCredentials =
                new NetworkCredential("field-user", "field-password", "field-domain");

            private static NetworkCredential PropertyCredentials { get; } =
                new("property-user", "property-password", "property-domain");

            public static int Run(int input) => input switch
            {
                0 => DefaultCarrier(),
                1 => ConstructorCarrier(),
                2 => CredentialParameterAndReturn(),
                3 => UriAbsoluteSemantics(),
                4 => UriRelativeSemantics(),
                5 => FieldAndPropertyCarriers(),
                6 => NullNormalization(),
                7 => CredentialAuthenticationTypeIsInert(),
                8 => HostCredentialInterfacePreservesIdentity(),
                9 => HostCredentialArgumentsAreInert(),
                10 => HostAndUriLookupsShareMutableValues(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int DefaultCarrier()
            {
                var credentials = new NetworkCredential();
                return Result(
                    credentials.UserName == string.Empty &&
                    credentials.Password == string.Empty &&
                    credentials.Domain == string.Empty,
                    100);
            }

            private static int ConstructorCarrier()
            {
                var credentials = new NetworkCredential("user", "password", "domain");
                return Result(
                    credentials.UserName == "user" &&
                    credentials.Password == "password" &&
                    credentials.Domain == "domain",
                    101);
            }

            private static int CredentialParameterAndReturn()
            {
                var uri = new Uri("https://example.com/xml?document=1");
                ICredentials input = new NetworkCredential("user", "password", "domain");
                var output = Resolve(input, uri, "Basic");
                return Result(
                    ReferenceEquals(input, output) &&
                    output.UserName == "user" &&
                    output.Password == "password" &&
                    output.Domain == "domain",
                    102);
            }

            private static NetworkCredential Resolve(
                ICredentials credentials,
                Uri uri,
                string authenticationType) =>
                credentials.GetCredential(uri, authenticationType)!;

            private static int UriAbsoluteSemantics()
            {
                var uri = new Uri("https://example.com/xml?document=1");
                return Result(
                    uri.IsAbsoluteUri &&
                    uri.Scheme == "https" &&
                    uri.Host == "example.com" &&
                    uri.AbsolutePath == "/xml" &&
                    uri.Query == "?document=1" &&
                    uri.AbsoluteUri == "https://example.com/xml?document=1",
                    103);
            }

            private static int UriRelativeSemantics()
            {
                var uri = new Uri("xml/document.xml", UriKind.Relative);
                return Result(
                    !uri.IsAbsoluteUri &&
                    uri.OriginalString == "xml/document.xml" &&
                    uri.ToString() == "xml/document.xml",
                    104);
            }

            private static int FieldAndPropertyCarriers()
            {
                var field = FieldCredentials.GetCredential(
                    new Uri("https://example.com"),
                    "Digest");
                return Result(
                    ReferenceEquals(field, FieldCredentials) &&
                    PropertyCredentials.UserName == "property-user" &&
                    PropertyCredentials.Password == "property-password" &&
                    PropertyCredentials.Domain == "property-domain",
                    105);
            }

            private static int NullNormalization()
            {
                var credentials = new NetworkCredential
                {
                    UserName = null,
                    Password = null,
                    Domain = null,
                };
                return Result(
                    credentials.UserName == string.Empty &&
                    credentials.Password == string.Empty &&
                    credentials.Domain == string.Empty,
                    106);
            }

            private static int CredentialAuthenticationTypeIsInert()
            {
                var credentials = new NetworkCredential("user", "password");
                var uri = new Uri("https://example.com");
                var basic = credentials.GetCredential(uri, "Basic");
                var unknown = credentials.GetCredential(uri, "Unknown");
                return Result(
                    ReferenceEquals(credentials, basic) &&
                    ReferenceEquals(credentials, unknown),
                    107);
            }

            private static int HostCredentialInterfacePreservesIdentity()
            {
                var credentials = new NetworkCredential("host-user", "host-password", "host-domain");
                ICredentialsByHost lookup = credentials;
                var resolved = lookup.GetCredential("example.com", 443, "Basic");
                return Result(
                    ReferenceEquals(credentials, resolved) &&
                    resolved!.UserName == "host-user" &&
                    resolved.Password == "host-password" &&
                    resolved.Domain == "host-domain",
                    108);
            }

            private static int HostCredentialArgumentsAreInert()
            {
                var credentials = new NetworkCredential();
                ICredentialsByHost lookup = credentials;
                int[] ports = [int.MinValue, -1, 0, 65535, 65536, int.MaxValue];
                foreach (var port in ports)
                {
                    if (!ReferenceEquals(credentials, credentials.GetCredential(null, port, null)) ||
                        !ReferenceEquals(credentials, lookup.GetCredential(null!, port, null!)) ||
                        !ReferenceEquals(credentials, lookup.GetCredential("not a host", port, "Unknown")) ||
                        !ReferenceEquals(credentials, lookup.GetCredential(string.Empty, port, string.Empty)))
                    {
                        return -109;
                    }
                }
                return 109;
            }

            private static int HostAndUriLookupsShareMutableValues()
            {
                var credentials = new NetworkCredential("user", "password", "domain");
                ICredentialsByHost lookup = credentials;
                var byHost = lookup.GetCredential("example.com", 80, "Digest")!;
                var byUri = credentials.GetCredential((Uri?)null, null);
                byHost.UserName = null;
                byUri.Password = null;
                credentials.Domain = null;
                return Result(
                    ReferenceEquals(byHost, byUri) &&
                    ReferenceEquals(credentials, byHost) &&
                    byHost.UserName == string.Empty &&
                    byHost.Password == string.Empty &&
                    byHost.Domain == string.Empty,
                    110);
            }

            private static int Result(bool passed, int value) => passed ? value : -value;
        }
        """;
}
