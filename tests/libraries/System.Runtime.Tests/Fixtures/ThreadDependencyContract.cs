// From the compiler harness's ThreadDependencyContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
using System.Security.Principal;
using System.Runtime.Serialization;
#pragma warning disable SYSLIB0050, SYSLIB0051
namespace ThreadDependencyFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var stack = CompressedStack.Capture();
        if (!ReferenceEquals(stack, stack.CreateCopy()) || ReferenceEquals(stack, CompressedStack.GetCompressedStack())) return 1;
        var marker = new object(); object? observed = null;
        CompressedStack.Run(stack, value => observed = value, marker);
        if (!ReferenceEquals(marker, observed)) return 2;
        var expected = new ArgumentException("callback");
        try { CompressedStack.Run(stack, _ => throw expected, null); return 3; } catch (ArgumentException e) { if (!ReferenceEquals(e, expected)) return 4; }
        try { CompressedStack.Run(null!, null!, null); return 5; } catch (ArgumentNullException e) { if (e.ParamName != "compressedStack") return 6; }
        try { CompressedStack.Run(stack, null!, null); return 7; } catch (NullReferenceException) { }
        try { stack.GetObjectData(null!, default); return 8; } catch (PlatformNotSupportedException) { }
        try { ((ISerializable)stack).GetObjectData(null!, default); return 9; } catch (PlatformNotSupportedException) { }
        IIdentity identity = new Identity(); IPrincipal principal = new Principal(identity);
        if (identity.Name != "example" || identity.AuthenticationType != "test" || !identity.IsAuthenticated) return 10;
        if (!ReferenceEquals(identity, principal.Identity) || !principal.IsInRole("reader") || principal.IsInRole("writer")) return 11;
        if (Slot(null) != null) return 12;
        return input + 1;
    }
    private static LocalDataStoreSlot? Slot(LocalDataStoreSlot? slot) => slot;
}
public sealed class Identity : IIdentity
{
    public string Name => "example";
    public string AuthenticationType => "test";
    public bool IsAuthenticated => true;
}
public sealed class Principal : IPrincipal
{
    public Principal(IIdentity identity) => Identity = identity;
    public IIdentity Identity { get; }
    public bool IsInRole(string role) => role == "reader";
}
