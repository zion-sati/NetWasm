// Sequential adaptation of the dotnet/runtime System.Private.CoreLib
// Interlocked contracts. Licensed under MIT by the .NET Foundation.
//
// NetWasm runs managed code in a single reactor with no concurrent managed
// memory access. Synchronous loads/stores therefore preserve these contracts;
// barriers need no operation. Shared-memory managed threads would require
// atomic lowering and real barriers before this support can be retained.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Threading;

public static class Interlocked
{
    public static int Increment(ref int location) => Add(ref location, 1);

    public static int Decrement(ref int location) => Add(ref location, -1);

    public static int Add(ref int location1, int value)
    {
        ValidateLocation(ref location1);
        return location1 = unchecked(location1 + value);
    }

    public static long Increment(ref long location) => Add(ref location, 1);

    public static long Decrement(ref long location) => Add(ref location, -1);

    public static long Add(ref long location1, long value)
    {
        ValidateLocation(ref location1);
        return location1 = unchecked(location1 + value);
    }

    public static int Exchange(ref int location1, int value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    public static long Exchange(ref long location1, long value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    public static nint Exchange(ref nint location1, nint value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    public static float Exchange(ref float location1, float value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    public static double Exchange(ref double location1, double value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    [return: NotNullIfNotNull(nameof(location1))]
    public static object? Exchange([NotNullIfNotNull(nameof(value))] ref object? location1, object? value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    // T is a reference, enum or primitive type, as upstream requires.
    public static T Exchange<T>(ref T location1, T value)
    {
        RequireReferenceEnumOrPrimitive<T>();
        ValidateLocation(ref location1);
        var previous = location1;
        location1 = value;
        return previous;
    }

    public static int CompareExchange(ref int location1, int value, int comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        if (previous == comparand)
        {
            location1 = value;
        }
        return previous;
    }

    public static long CompareExchange(ref long location1, long value, long comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        if (previous == comparand)
        {
            location1 = value;
        }
        return previous;
    }

    public static nint CompareExchange(ref nint location1, nint value, nint comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        if (previous == comparand)
        {
            location1 = value;
        }
        return previous;
    }

    public static float CompareExchange(ref float location1, float value, float comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        // Compare the stored representation: equal NaN payloads match, while
        // positive and negative zero differ. Numeric equality is not sufficient.
        if (BitConverter.SingleToInt32Bits(previous) == BitConverter.SingleToInt32Bits(comparand))
        {
            location1 = value;
        }
        return previous;
    }

    public static double CompareExchange(ref double location1, double value, double comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        // Compare the stored representation: equal NaN payloads match, while
        // positive and negative zero differ. Numeric equality is not sufficient.
        if (BitConverter.DoubleToInt64Bits(previous) == BitConverter.DoubleToInt64Bits(comparand))
        {
            location1 = value;
        }
        return previous;
    }

    [return: NotNullIfNotNull(nameof(location1))]
    public static object? CompareExchange(ref object? location1, object? value, object? comparand)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        if (ReferenceEquals(previous, comparand))
        {
            location1 = value;
        }
        return previous;
    }

    [return: NotNullIfNotNull(nameof(location1))]
    public static T CompareExchange<T>(ref T location1, T value, T comparand)
    {
        RequireReferenceEnumOrPrimitive<T>();
        ValidateLocation(ref location1);
        var previous = location1;
        if (typeof(T).IsValueType ? BitwiseEquals(previous, comparand) : ReferenceEquals(previous, comparand))
        {
            location1 = value;
        }
        return previous;
    }

    public static long Read(ref long location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void MemoryBarrier() { }

    public static void MemoryBarrierProcessWide() { }

    public static int And(ref int location1, int value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 &= value;
        return previous;
    }

    [CLSCompliant(false)]
    public static uint And(ref uint location1, uint value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 &= value;
        return previous;
    }

    public static long And(ref long location1, long value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 &= value;
        return previous;
    }

    [CLSCompliant(false)]
    public static ulong And(ref ulong location1, ulong value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 &= value;
        return previous;
    }

    public static int Or(ref int location1, int value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 |= value;
        return previous;
    }

    [CLSCompliant(false)]
    public static uint Or(ref uint location1, uint value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 |= value;
        return previous;
    }

    public static long Or(ref long location1, long value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 |= value;
        return previous;
    }

    [CLSCompliant(false)]
    public static ulong Or(ref ulong location1, ulong value)
    {
        ValidateLocation(ref location1);
        var previous = location1;
        location1 |= value;
        return previous;
    }
    // The integer-primitive and integer-enum forms of dotnet/runtime 474ed963's generic And and Or,
    // which System.Net.Http's header flags use. As the sized forms above, each combines in place.
    public static T And<T>(ref T location1, T value) where T : struct
    {
        RequireIntegerOrEnum<T>();
        ValidateLocation(ref location1);
        T previous = location1;
        if (Unsafe.SizeOf<T>() == 1)
        {
            Unsafe.As<T, byte>(ref location1) = (byte)(Unsafe.BitCast<T, byte>(previous) & Unsafe.BitCast<T, byte>(value));
        }
        else if (Unsafe.SizeOf<T>() == 2)
        {
            Unsafe.As<T, ushort>(ref location1) = (ushort)(Unsafe.BitCast<T, ushort>(previous) & Unsafe.BitCast<T, ushort>(value));
        }
        else if (Unsafe.SizeOf<T>() == 4)
        {
            Unsafe.As<T, uint>(ref location1) = Unsafe.BitCast<T, uint>(previous) & Unsafe.BitCast<T, uint>(value);
        }
        else
        {
            // As upstream: every integer primitive and integer enum left is eight bytes.
            Unsafe.As<T, ulong>(ref location1) = Unsafe.BitCast<T, ulong>(previous) & Unsafe.BitCast<T, ulong>(value);
        }

        return previous;
    }

    public static T Or<T>(ref T location1, T value) where T : struct
    {
        RequireIntegerOrEnum<T>();
        ValidateLocation(ref location1);
        T previous = location1;
        if (Unsafe.SizeOf<T>() == 1)
        {
            Unsafe.As<T, byte>(ref location1) = (byte)(Unsafe.BitCast<T, byte>(previous) | Unsafe.BitCast<T, byte>(value));
        }
        else if (Unsafe.SizeOf<T>() == 2)
        {
            Unsafe.As<T, ushort>(ref location1) = (ushort)(Unsafe.BitCast<T, ushort>(previous) | Unsafe.BitCast<T, ushort>(value));
        }
        else if (Unsafe.SizeOf<T>() == 4)
        {
            Unsafe.As<T, uint>(ref location1) = Unsafe.BitCast<T, uint>(previous) | Unsafe.BitCast<T, uint>(value);
        }
        else
        {
            // As upstream: every integer primitive and integer enum left is eight bytes.
            Unsafe.As<T, ulong>(ref location1) = Unsafe.BitCast<T, ulong>(previous) | Unsafe.BitCast<T, ulong>(value);
        }

        return previous;
    }

    // As upstream: integer primitives and enums backed by them; floating point is not.
    private static void RequireIntegerOrEnum<T>()
    {
        if ((!typeof(T).IsPrimitive && !typeof(T).IsEnum) ||
            typeof(T) == typeof(float) || typeof(T) == typeof(double) ||
            (typeof(T).IsEnum && (typeof(T).GetEnumUnderlyingType() == typeof(float) ||
                typeof(T).GetEnumUnderlyingType() == typeof(double))))
        {
            throw new NotSupportedException(SR.NotSupported_IntegerEnumOrPrimitiveTypeRequired);
        }
    }

    private static void RequireReferenceEnumOrPrimitive<T>()
    {
        if (typeof(T).IsValueType && !typeof(T).IsPrimitive && !typeof(T).IsEnum)
        {
            throw new NotSupportedException(SR.NotSupported_ReferenceEnumOrPrimitiveTypeRequired);
        }
    }

    private static void ValidateLocation<T>(ref T location)
    {
        // A managed null byref is address zero in Wasm memory, not a hardware
        // fault. Reject it before any load or store to preserve .NET behavior.
        if (Unsafe.IsNullRef(ref location))
        {
            throw new NullReferenceException();
        }
    }

    // An enum or primitive compares by its bits, as the atomic compare does.
    private static bool BitwiseEquals<T>(T left, T right) => Unsafe.SizeOf<T>() switch
    {
        1 => Unsafe.As<T, byte>(ref left) == Unsafe.As<T, byte>(ref right),
        2 => Unsafe.As<T, ushort>(ref left) == Unsafe.As<T, ushort>(ref right),
        4 => Unsafe.As<T, uint>(ref left) == Unsafe.As<T, uint>(ref right),
        _ => Unsafe.As<T, ulong>(ref left) == Unsafe.As<T, ulong>(ref right),
    };
}
