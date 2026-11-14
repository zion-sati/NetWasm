// From the compiler harness's AppDomainPrincipalContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Security.Principal;
using System.Threading;
namespace DomainPrincipalFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var domain = AppDomain.CurrentDomain;
        Thread.CurrentPrincipal = null;
        domain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);
        try { _ = Thread.CurrentPrincipal; return 20; } catch (PlatformNotSupportedException) { }
        domain.SetPrincipalPolicy(PrincipalPolicy.UnauthenticatedPrincipal);
        if (Thread.CurrentPrincipal is not GenericPrincipal { Identity: GenericIdentity { Name: "", IsAuthenticated: false } }) return 21;
        Thread.CurrentPrincipal = null;
        domain.SetPrincipalPolicy(PrincipalPolicy.NoPrincipal);
        return Portable(input);
    }
    public static int Portable(int input)
    {
        if ((int)PrincipalPolicy.UnauthenticatedPrincipal != 0 || (int)PrincipalPolicy.NoPrincipal != 1 || (int)PrincipalPolicy.WindowsPrincipal != 2) return 1;
        var domain = AppDomain.CurrentDomain;
        domain.SetPrincipalPolicy(PrincipalPolicy.NoPrincipal); Thread.CurrentPrincipal = null;
        if (Thread.CurrentPrincipal != null) return 2;
        domain.SetPrincipalPolicy((PrincipalPolicy)99);
        if (Thread.CurrentPrincipal != null) return 3;
        var explicitPrincipal = new Principal("explicit");
        Thread.CurrentPrincipal = explicitPrincipal;
        domain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);
        if (!ReferenceEquals(Thread.CurrentPrincipal, explicitPrincipal)) return 4;
        domain.SetPrincipalPolicy(PrincipalPolicy.NoPrincipal);
        try { domain.SetThreadPrincipal(null!); return 5; } catch (ArgumentNullException e) { if (e.ParamName != "principal") return 6; }
        var defaultPrincipal = new Principal("default");
        domain.SetThreadPrincipal(defaultPrincipal);
        if (!ReferenceEquals(Thread.CurrentPrincipal, explicitPrincipal)) return 7;
        Thread.CurrentPrincipal = null;
        if (!ReferenceEquals(Thread.CurrentPrincipal, defaultPrincipal)) return 8;
        try { domain.SetThreadPrincipal(new Principal("second")); return 9; } catch (SystemException) { }
        Thread.CurrentPrincipal = explicitPrincipal;
        var context = ExecutionContext.Capture()!;
        Thread.CurrentPrincipal = defaultPrincipal;
        var observed = false;
        ExecutionContext.Run(context, _ => { observed = ReferenceEquals(Thread.CurrentPrincipal, explicitPrincipal); Thread.CurrentPrincipal = null; if (!ReferenceEquals(Thread.CurrentPrincipal, defaultPrincipal)) observed = false; }, null);
        if (!observed || !ReferenceEquals(Thread.CurrentPrincipal, defaultPrincipal)) return 10;
        domain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal); Thread.CurrentPrincipal = null;
        if (!ReferenceEquals(Thread.CurrentPrincipal, defaultPrincipal)) return 11;
        return input + 1;
    }
    private sealed class Principal : IPrincipal
    {
        private readonly string _role;
        public Principal(string role) { _role = role; }
        public IIdentity? Identity => null;
        public bool IsInRole(string role) => role == _role;
    }
}
