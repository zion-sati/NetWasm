// From the compiler harness's HttpListenerBoundaryCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Net;
using System.Security.Authentication.ExtendedProtection;
namespace HttpListenerBoundaryFixture;

public static class EntryPoint
{
    public static int Run(int value)
    {
        AuthenticationSchemeSelector selector = request => AuthenticationSchemes.Basic;
        if (selector(null!) != AuthenticationSchemes.Basic) return 1;
        var policy = new ExtendedProtectionPolicy(PolicyEnforcement.Never);
        HttpListener.ExtendedProtectionSelector protection = request => policy;
        if (!ReferenceEquals(protection(null!), policy)) return 2;

        // Upstream's managed listener: supported, and a new listener has no prefixes until started.
        if (!HttpListener.IsSupported) return 3;
        using (var listener = new HttpListener())
        {
            if (listener.IsListening || listener.Prefixes.Count != 0 || listener.AuthenticationSchemes != AuthenticationSchemes.Anonymous) return 4;
        }

        return value + 1;
    }
}
