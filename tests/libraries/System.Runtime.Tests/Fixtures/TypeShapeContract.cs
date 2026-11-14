// From the compiler harness's TypeShapeContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
namespace TypeShapeFixture;
public static class EntryPoint
{
    public static unsafe int Run(int input)
    {
        Type[] primitive = { typeof(bool), typeof(char), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(nint), typeof(nuint) };
        foreach (var type in primitive)
            if (type.IsSignatureType || !type.IsPrimitive || !type.IsValueType || type.IsArray || type.IsPointer || type.IsByRef) return 1;
        Type[] other = { typeof(decimal), typeof(void), typeof(string), typeof(object), typeof(E), typeof(int?), typeof(int[]), typeof(int[,]), typeof(int*), typeof(Pair<>), typeof(Pair<int>) };
        foreach (var type in other)
            if (type.IsPrimitive || type.IsSignatureType) return 2;
        if (!typeof(decimal).IsValueType || !typeof(E).IsValueType || !typeof(int?).IsValueType || typeof(string).IsValueType) return 3;
        if (!typeof(int[]).IsArray || !typeof(int[,]).IsArray || typeof(int*).IsArray || !typeof(int*).IsPointer) return 4;
        if (Type.GetTypeCode(null) != TypeCode.Empty || Type.GetTypeCode(typeof(E)) != TypeCode.Int16 || Type.GetTypeCode(typeof(int?)) != TypeCode.Object) return 5;
        if (typeof(MarshalByRefObject).IsMarshalByRef || typeof(Remote).IsMarshalByRef ||
            typeof(object).IsMarshalByRef || typeof(Remote[]).IsMarshalByRef || typeof(int).IsMarshalByRef) return 6;
        if (new Remote().GetType() != typeof(Remote)) return 7;
        if (typeof(int).GetType() != ((object)typeof(int)).GetType()) return 8;
        return input + 1;
    }
    private enum E : short { One = 1 }
    private class Pair<T> { }
    private sealed class Remote : MarshalByRefObject { }
}
