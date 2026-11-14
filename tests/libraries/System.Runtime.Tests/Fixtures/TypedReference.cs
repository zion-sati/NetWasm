// From the compiler harness's TypedReferenceCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace TypedReferenceFixture;

public static class EntryPoint
{
    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    // C# does not allow TypedReference as a return type, so the value crosses a call as a parameter.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsEmpty(TypedReference value) =>
        TypedReference.GetTargetType(value) == null && value.GetHashCode() == 0;

    public static int Run(int input)
    {
        TypedReference empty = default;
        if (!IsEmpty(empty)) return 1;
        if (!Throws<NullReferenceException>(() => TypedReference.TargetTypeToken(default))) return 2;
        if (!Throws<ArgumentNullException>(() => TypedReference.ToObject(default))) return 3;
        if (!Throws<NotSupportedException>(() => TypedReference.SetTypedReference(default, 1))) return 4;
        if (!Throws<NotSupportedException>(() => default(TypedReference).Equals(null))) return 5;
        if (!Throws<ArgumentNullException>(() => TypedReference.MakeTypedReference(null!, new FieldInfo[1])) ||
            !Throws<ArgumentNullException>(() => TypedReference.MakeTypedReference(new object(), null!)) ||
            !Throws<ArgumentException>(() => TypedReference.MakeTypedReference(new object(), Array.Empty<FieldInfo>()))) return 6;

#if !DESKTOP_ORACLE
        if (!Throws<PlatformNotSupportedException>(() => TypedReference.MakeTypedReference(new object(), new FieldInfo[1]))) return 7;
#else
        if (!Throws<ArgumentException>(() => TypedReference.MakeTypedReference(new object(), new FieldInfo[1]))) return 7;
#endif

        return input + 1;
    }
}
