// From the compiler harness's TypeSecurityContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeSecurityFixture;
public static class EntryPoint
{
    public static unsafe int Run(int input)
    {
        Type[] types = { typeof(int), typeof(string), typeof(object), typeof(void), typeof(int[]), typeof(int[,]), typeof(int*), typeof(Value), typeof(Box<>), typeof(Box<int>), typeof(IMarker), typeof(Span<int>), typeof(Box<>).GetGenericArguments()[0] };
        foreach (var type in types)
            if (!type.IsSecurityCritical || type.IsSecuritySafeCritical || type.IsSecurityTransparent) return 1;
        return input + 1;
    }
    private struct Value { }
    private class Box<T> { }
    private interface IMarker { }
}
