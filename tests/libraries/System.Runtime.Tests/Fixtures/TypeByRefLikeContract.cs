// From the compiler harness's TypeByRefLikeContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeByRefLikeFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        Type[] refTypes = { typeof(Span<int>), typeof(ReadOnlySpan<byte>), typeof(Span<>), typeof(RefValue) };
        foreach (var type in refTypes)
            if (!type.IsByRefLike || !type.IsValueType) return 1;
        Type[] ordinary = { typeof(int), typeof(string), typeof(int[]), typeof(Value), typeof(Box<int>), typeof(Box<>) };
        foreach (var type in ordinary)
            if (type.IsByRefLike) return 2;
        return input + 1;
    }
    private ref struct RefValue { }
    private struct Value { }
    private class Box<T> { }
}
