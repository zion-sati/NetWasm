using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NetPrimitivesCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CookiesCredentialsHeadersEncodingAndMailMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NetPrimitivesFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NetPrimitivesFixture.EntryPoint",
            "Run", [new("run", "NetPrimitivesFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Collections.Specialized;
        using System.Net;
        using System.Net.Cache;
        using System.Net.Mail;
        using System.Net.Mime;
        using System.Net.Security;
        using System.Security.Authentication;
        using System.Security.Authentication.ExtendedProtection;
        using System.Web;
        namespace NetPrimitivesFixture;

        public static class EntryPoint
        {
            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; } catch (T) { return true; }
            }

            public static int Run(int input)
            {
                // Cookies keep their attributes, format and validation.
                var cookie = new Cookie("name", "value", "/", "example.com");
                if (cookie.Name != "name" || cookie.Value != "value" || cookie.Path != "/" || cookie.Domain != "example.com" || cookie.ToString() != "name=value") return 1;
                if (!cookie.Equals(new Cookie("name", "value", "/", "example.com")) || cookie.Expired || cookie.HttpOnly) return 2;
                if (!Throws<CookieException>(() => new Cookie("bad;name", "v"))) return 3;

                // A container matches cookies by domain and path, most specific path first.
                var container = new CookieContainer();
                container.Add(new Uri("http://example.com/app/"), new Cookie("session", "abc", "/app"));
                container.SetCookies(new Uri("http://example.com/"), "theme=dark; path=/");
                if (container.Count != 2 || container.GetCookieHeader(new Uri("http://example.com/app/page")) != "session=abc; theme=dark") return 4;
                if (container.GetCookies(new Uri("http://example.com/")).Count != 1 || container.GetCookieHeader(new Uri("http://other.com/")) != "") return 5;

                var collection = new CookieCollection { new Cookie("x", "y") };
                if (collection["x"]?.Value != "y" || collection.Count != 1) return 6;

                // Credentials are cached per URI prefix and scheme.
                var credential = new NetworkCredential("user", "secret", "domain");
                var cache = new CredentialCache();
                cache.Add(new Uri("http://example.com/"), "Basic", credential);
                if (!ReferenceEquals(cache.GetCredential(new Uri("http://example.com/x"), "Basic"), credential) || cache.GetCredential(new Uri("http://other.com/"), "Basic") != null) return 7;
                if (credential.SecurePassword.Length != 6 || credential.Password != "secret" || credential.Domain != "domain") return 8;
                if (HttpVersion.Version11 != new Version(1, 1) || HttpVersion.Version20 != new Version(2, 0)) return 9;

                // WebHeaderCollection validates names and formats its contents.
                var headers = new WebHeaderCollection();
                headers.Add("X-Test: a");
                headers[HttpRequestHeader.ContentType] = "text/plain";
                if (headers["X-Test"] != "a" || headers[HttpRequestHeader.ContentType] != "text/plain" || headers.Count != 2) return 10;
                if (headers.ToString() != "X-Test: a\r\nContent-Type: text/plain\r\n\r\n") return 11;
                if (!Throws<ArgumentException>(() => headers.Add("bad name: v"))) return 12;

                // HttpUtility encodes and parses as upstream does.
                if (HttpUtility.UrlEncode("a b&c") != "a+b%26c" || HttpUtility.UrlDecode("a+b%26c") != "a b&c") return 13;
                if (HttpUtility.HtmlEncode("<a href=\"x\">") != "&lt;a href=&quot;x&quot;&gt;" || HttpUtility.HtmlDecode("&lt;b&gt;") != "<b>") return 14;
                NameValueCollection query = HttpUtility.ParseQueryString("?a=1&b=two%20words&a=3");
                if (query["a"] != "1,3" || query["b"] != "two words") return 15;
                if (HttpUtility.JavaScriptStringEncode("it's \"x\"") != "it\\u0027s \\\"x\\\"") return 16;

                // Protocol enums keep their Windows-defined values.
                if ((int)SslProtocols.Tls12 != 3072 || (int)CipherAlgorithmType.Aes256 != 26128 || (int)HashAlgorithmType.Sha256 != 32780) return 17;
                if ((int)ExchangeAlgorithmType.RsaSign != 9216 || (int)SslPolicyErrors.RemoteCertificateChainErrors != 4 || (int)AuthenticationLevel.MutualAuthRequired != 2) return 18;
                if (new RequestCachePolicy(RequestCacheLevel.Reload).Level != RequestCacheLevel.Reload || (int)RequestCacheLevel.NoCacheNoStore != 6 || (int)ChannelBindingKind.Endpoint != 26) return 19;

                // Mail addresses parse and format.
                var address = new MailAddress("Joe Bloggs <joe@example.com>");
                if (address.DisplayName != "Joe Bloggs" || address.Address != "joe@example.com" || address.User != "joe" || address.Host != "example.com") return 20;
                if (!Throws<FormatException>(() => new MailAddress("not an address"))) return 21;

                // Messages carry their recipients, bodies, views and attachments.
                using var message = new MailMessage("from@example.com", "to@example.com", "Subject", "Body");
                message.CC.Add("a@example.com, b@example.com");
                message.Priority = MailPriority.High;
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString("<b>x</b>", null, MediaTypeNames.Text.Html));
                message.Attachments.Add(Attachment.CreateAttachmentFromString("data", "a.txt"));
                if (message.To[0].Address != "to@example.com" || message.CC.Count != 2 || message.Priority != MailPriority.High || message.IsBodyHtml) return 22;
                if (message.AlternateViews[0].ContentType.MediaType != "text/html" || message.Attachments[0].Name != "a.txt") return 23;

                // Subjects and header values pass through Unicode normalization.
                message.Subject = "Gr\u00fc\u00dfe";
                message.Headers.Add("X-Custom", "caf\u00e9");
                if (message.Subject != "Gr\u00fc\u00dfe" || message.Headers["X-Custom"] != "caf\u00e9") return 29;

                // MIME headers parse and normalize.
                var type = new ContentType("text/plain; charset=utf-8; name=\"x.txt\"");
                if (type.MediaType != "text/plain" || type.CharSet != "utf-8" || type.Name != "x.txt") return 24;
                var disposition = new ContentDisposition("attachment; filename=\"report.pdf\"; size=42");
                if (disposition.DispositionType != DispositionTypeNames.Attachment || disposition.FileName != "report.pdf" || disposition.Size != 42 || disposition.Inline) return 25;
                if (MediaTypeNames.Application.Json != "application/json" || TransferEncoding.Base64 != (TransferEncoding)1) return 26;
                if (new SmtpException(SmtpStatusCode.MailboxBusy, "busy").StatusCode != SmtpStatusCode.MailboxBusy || (int)SmtpStatusCode.MailboxUnavailable != 550) return 27;
                using (var client = new SmtpClient("localhost"))
                {
                    if (client.Host != "localhost" || client.Port != 25 || client.DeliveryMethod != SmtpDeliveryMethod.Network) return 28;
                }
                return input + 1;
            }
        }
        """;
}
