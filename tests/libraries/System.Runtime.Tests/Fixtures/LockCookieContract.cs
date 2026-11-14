// From the compiler harness's LockCookieContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
namespace LockCookieFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var first = default(LockCookie);
        var second = new LockCookie();
        if (!first.Equals(second) || !(first == second) || first != second) return 1;
        object boxed = second;
        if (!first.Equals(boxed) || first.Equals((object?)null) || first.Equals(new object())) return 2;
        if (first.GetHashCode() != 0 || first.GetHashCode() != second.GetHashCode()) return 3;
        IEquatable<LockCookie> comparable = first;
        if (!comparable.Equals(second) || !((LockCookie)boxed).Equals(first)) return 4;
        var values = new LockCookie[2];
        values[1] = first;
        if (values[0] != values[1]) return 5;
        return input + 1;
    }
}
