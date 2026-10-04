// Sequential adaptation of the dotnet/runtime System.Private.CoreLib
// Volatile contracts. Licensed under MIT by the .NET Foundation.
//
// NetWasm executes managed code in one reactor without concurrent managed
// memory access. Ordinary loads and stores preserve the sequential contract,
// including 64-bit values on wasm32. Shared-memory managed threads would
// require atomic accesses and acquire/release ordering before enabling them.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Threading;

public static class Volatile
{
    public static bool Read(ref bool location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref bool location, bool value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static byte Read(ref byte location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref byte location, byte value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [CLSCompliant(false)]
    public static sbyte Read(ref sbyte location)
    {
        ValidateLocation(ref location);
        return location;
    }

    [CLSCompliant(false)]
    public static void Write(ref sbyte location, sbyte value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static short Read(ref short location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref short location, short value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [CLSCompliant(false)]
    public static ushort Read(ref ushort location)
    {
        ValidateLocation(ref location);
        return location;
    }

    [CLSCompliant(false)]
    public static void Write(ref ushort location, ushort value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static int Read(ref int location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref int location, int value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [CLSCompliant(false)]
    public static uint Read(ref uint location)
    {
        ValidateLocation(ref location);
        return location;
    }

    [CLSCompliant(false)]
    public static void Write(ref uint location, uint value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static long Read(ref long location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref long location, long value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [CLSCompliant(false)]
    public static ulong Read(ref ulong location)
    {
        ValidateLocation(ref location);
        return location;
    }

    [CLSCompliant(false)]
    public static void Write(ref ulong location, ulong value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static nint Read(ref nint location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref nint location, nint value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [CLSCompliant(false)]
    public static nuint Read(ref nuint location)
    {
        ValidateLocation(ref location);
        return location;
    }

    [CLSCompliant(false)]
    public static void Write(ref nuint location, nuint value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static float Read(ref float location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref float location, float value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    public static double Read(ref double location)
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write(ref double location, double value)
    {
        ValidateLocation(ref location);
        location = value;
    }

    [return: NotNullIfNotNull(nameof(location))]
    public static T Read<T>(ref T location) where T : class?
    {
        ValidateLocation(ref location);
        return location;
    }

    public static void Write<T>([NotNullIfNotNull(nameof(value))] ref T location, T value) where T : class?
    {
        ValidateLocation(ref location);
        location = value;
    }

    private static void ValidateLocation<T>(ref T location)
    {
        // Address zero is accessible Wasm memory. Check before the load/store
        // so a null managed byref throws instead of accessing runtime memory.
        if (Unsafe.IsNullRef(ref location))
        {
            throw new NullReferenceException();
        }
    }
}
