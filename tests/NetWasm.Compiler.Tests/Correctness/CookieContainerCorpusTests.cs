using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// CookieContainer and CookieCollection against desktop: path ordering in headers, attribute parsing, domain and
// host-only matching, per-domain and total capacity, expiry, RFC 2965 ports, replacement, size limits and the
// collection's own operations. Each input returns a hash of what the scenario observed; where desktop's order is
// unspecified (cookies across domains), the observations are sorted first.
public sealed class CookieContainerCorpusTests(CorrectnessTestRunner runner)
{
    private const string Namespace = "NetWasm.Correctness.Cookies";

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Net;

        namespace NetWasm.Correctness.Cookies;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var seen = new List<string>();
                try
                {
                    Scenario(input, seen);
                }
                catch (Exception exception)
                {
                    // Classified by type test: a program without type names reads type#<id> for a name.
                    seen.Add("threw " + (exception is CookieException ? "cookie" : exception is ArgumentException ? "argument" :
                        exception is FormatException ? "format" : "other"));
                }
                var hash = 17;
                foreach (var line in seen)
                {
                    foreach (var c in line)
                    {
                        hash = unchecked(hash * 31 + c);
                    }
                    hash = unchecked(hash * 31 + '|');
                }
                return hash & 0x7fffffff;
            }

            private static void Scenario(int input, List<string> seen)
            {
                var site = new Uri("http://example.com/");
                switch (input)
                {
                    case 0:
                    {
                        // Longer paths come first in a header.
                        var container = new CookieContainer();
                        container.Add(new Uri("http://example.com/a/b/"), new Cookie("deep", "1", "/a/b"));
                        container.Add(new Uri("http://example.com/a/"), new Cookie("mid", "2", "/a"));
                        container.Add(site, new Cookie("root", "3", "/"));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com/a/b/c")));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com/a/x")));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com/z")));
                        seen.Add(container.Count.ToString());
                        break;
                    }
                    case 1:
                    {
                        // A Set-Cookie header with several cookies and their attributes.
                        var container = new CookieContainer();
                        // Expiry relative to the clock the program runs with.
                        container.SetCookies(site,
                            "a=1; Path=/; HttpOnly, b=2; Path=/; Secure, c=3; Path=/; Expires=" + DateTime.UtcNow.AddDays(30).ToString("r") + ", " +
                            "d=4; Path=/; Expires=" + DateTime.UtcNow.AddDays(-30).ToString("r"));
                        seen.Add(container.GetCookieHeader(site));
                        seen.Add(container.GetCookieHeader(new Uri("https://example.com/")));
                        foreach (Cookie cookie in container.GetCookies(new Uri("https://example.com/")))
                        {
                            seen.Add(cookie.Name + cookie.HttpOnly + cookie.Secure + cookie.Expired + cookie.Path + cookie.Domain);
                            seen.Add((cookie.Expires == DateTime.MinValue).ToString());
                        }
                        break;
                    }
                    case 2:
                    {
                        // Domain cookies reach subdomains; host-only cookies do not.
                        var container = new CookieContainer();
                        container.SetCookies(new Uri("http://www.example.com/"), "host=1; Path=/, shared=2; Domain=example.com; Path=/");
                        seen.Add(container.GetCookieHeader(new Uri("http://www.example.com/")));
                        seen.Add(container.GetCookieHeader(new Uri("http://a.b.example.com/")));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com/")));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.org/")));
                        break;
                    }
                    case 3:
                    {
                        // The per-domain capacity drops the oldest cookies.
                        var container = new CookieContainer(10, 3, 4096);
                        for (var i = 0; i < 5; i++)
                        {
                            container.Add(site, new Cookie("k" + i, "v" + i, "/"));
                        }
                        seen.Add(container.Count.ToString());
                        seen.Add(container.GetCookieHeader(site));
                        break;
                    }
                    case 4:
                    {
                        // The total capacity, across domains. Which domain loses its cookie is not deterministic on
                        // desktop (unused collections tie on their timestamp and the domain table's order follows
                        // randomized string hashes), so only the counts and the newest cookie are observed.
                        var container = new CookieContainer(4, 4, 4096);
                        for (var i = 0; i < 6; i++)
                        {
                            container.Add(new Uri("http://d" + i + ".example/"), new Cookie("k", "v" + i, "/"));
                        }
                        seen.Add(container.Count.ToString());
                        var kept = 0;
                        for (var i = 0; i < 6; i++)
                        {
                            kept += container.GetCookieHeader(new Uri("http://d" + i + ".example/")).Length == 0 ? 0 : 1;
                        }
                        seen.Add(kept.ToString());
                        seen.Add(container.GetCookieHeader(new Uri("http://d5.example/")));
                        break;
                    }
                    case 5:
                    {
                        // Expired cookies leave the container.
                        var container = new CookieContainer();
                        container.Add(site, new Cookie("old", "1", "/") { Expires = DateTime.UtcNow.AddDays(-30) });
                        container.Add(site, new Cookie("new", "2", "/") { Expires = DateTime.UtcNow.AddDays(30) });
                        seen.Add(container.GetCookieHeader(site));
                        seen.Add(container.Count.ToString());
                        break;
                    }
                    case 6:
                    {
                        // RFC 2965 cookies with a port list.
                        var container = new CookieContainer();
                        container.Add(new Uri("http://example.com:8080/"), new Cookie("p", "1", "/", "example.com") { Version = 1, Port = "\"80,8080\"" });
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com:8080/")));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com:9090/")));
                        break;
                    }
                    case 7:
                    {
                        // A cookie with the same name, path and domain replaces the earlier one.
                        var container = new CookieContainer();
                        container.SetCookies(site, "x=1; Path=/");
                        container.SetCookies(site, "x=2; Path=/");
                        container.SetCookies(site, "x=3; Path=/other");
                        seen.Add(container.GetCookieHeader(site));
                        seen.Add(container.GetCookieHeader(new Uri("http://example.com/other/")));
                        seen.Add(container.Count.ToString());
                        break;
                    }
                    case 8:
                    {
                        // All cookies, across domains, sorted because desktop's order is unspecified.
                        var container = new CookieContainer();
                        container.Add(new Uri("http://one.example/"), new Cookie("a", "1", "/"));
                        container.Add(new Uri("http://two.example/"), new Cookie("b", "2", "/"));
                        container.Add(new Uri("http://two.example/p/"), new Cookie("c", "3", "/p"));
                        var all = new List<string>();
                        foreach (Cookie cookie in container.GetAllCookies())
                        {
                            all.Add(cookie.Domain + cookie.Path + cookie.Name + cookie.Value);
                        }
                        all.Sort(StringComparer.Ordinal);
                        seen.AddRange(all);
                        break;
                    }
                    case 9:
                    {
                        // A cookie larger than the container's limit.
                        var container = new CookieContainer(10, 5, 16);
                        container.Add(site, new Cookie("big", new string('v', 40), "/"));
                        break;
                    }
                    case 10:
                    {
                        // CookieCollection: order, replacement, lookup by name, removal and copying.
                        var collection = new CookieCollection
                        {
                            new Cookie("a", "1", "/", "example.com"),
                            new Cookie("b", "2", "/", "example.com"),
                            new Cookie("a", "3", "/", "example.com"),
                        };
                        seen.Add(collection.Count.ToString());
                        foreach (Cookie cookie in collection)
                        {
                            seen.Add(cookie.Name + cookie.Value);
                        }
                        seen.Add(collection["b"]?.Value ?? "none");
                        seen.Add(collection.Contains(new Cookie("a", "1", "/", "example.com")).ToString());
                        seen.Add(collection.Remove(new Cookie("b", "2", "/", "example.com")).ToString());
                        var copy = new Cookie[collection.Count];
                        collection.CopyTo(copy, 0);
                        foreach (var cookie in copy)
                        {
                            seen.Add(cookie.Name + cookie.Value);
                        }
                        seen.Add(collection.IsReadOnly.ToString());
                        break;
                    }
                    case 11:
                    {
                        // GetCookies returns a collection that later container changes do not reach.
                        var container = new CookieContainer();
                        container.SetCookies(site, "a=1; Path=/");
                        var before = container.GetCookies(site);
                        container.SetCookies(site, "b=2; Path=/");
                        seen.Add(before.Count + " " + container.GetCookies(site).Count);
                        break;
                    }
                }
            }
        }
        """;

    // Cookie expiry reads the clock, so the program needs the platform world.
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void CookieContainerMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new CorpusFixture("CookieContainerCorpus" + optimization, Namespace, Source, [.. Enumerable.Range(0, 12)])
        {
            RequiresReactor = true,
            ReportAllMismatches = true,
            ExecuteOptimizedWasm = true,
            Optimization = optimization,
        });
}
