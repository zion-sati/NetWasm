// From the compiler harness's RuntimeArgumentHandleContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace ArgumentHandleFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var handle = default(RuntimeArgumentHandle);
        var copy = handle;
        if (!typeof(RuntimeArgumentHandle).IsValueType || !typeof(RuntimeArgumentHandle).IsByRefLike) return 1;
        return Accept(copy, input);
    }
    private static int Accept(RuntimeArgumentHandle handle, int input) => input + 1;
}
