// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// UIntPtr generic conversion contracts are retained from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 474ed963699fc29809015392738a15231aaae2de). One CoreLib serves wasm32 and
// wasm64, so the bounds upstream takes from its compile-time nuint_t alias
// are the runtime MinValue and MaxValue.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

public readonly partial struct UIntPtr
{
    /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        nuint result;

        if (typeof(TOther) == typeof(nuint))
        {
            result = (nuint)(object)value;
        }
        else if (!TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        nuint result;

        if (typeof(TOther) == typeof(nuint))
        {
            result = (nuint)(object)value;
        }
        else if (!TryConvertFromSaturating(value, out result) && !TOther.TryConvertToSaturating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        nuint result;

        if (typeof(TOther) == typeof(nuint))
        {
            result = (nuint)(object)value;
        }
        else if (!TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertFromChecked<TOther>(TOther value, out nuint result) => TryConvertFromChecked(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromChecked<TOther>(TOther value, out nuint result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = checked((nuint)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = checked((nuint)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = checked((nuint)actualValue);
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromSaturating{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertFromSaturating<TOther>(TOther value, out nuint result) => TryConvertFromSaturating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromSaturating<TOther>(TOther value, out nuint result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= (ulong)MaxValue) ? MaxValue :
                     (actualValue <= (ulong)MinValue) ? MinValue : (nuint)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (actualValue >= (ulong)MaxValue) ? MaxValue : (nuint)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (actualValue >= (ulong)MaxValue) ? MaxValue : (nuint)actualValue;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromTruncating{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertFromTruncating<TOther>(TOther value, out nuint result) => TryConvertFromTruncating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromTruncating<TOther>(TOther value, out nuint result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= (ulong)MaxValue) ? MaxValue :
                     (actualValue <= (ulong)MinValue) ? MinValue : (nuint)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (nuint)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (nuint)actualValue;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToChecked{TOther}(TSelf, out TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertToChecked<TOther>(nuint value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = (Half)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = checked((short)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualResult = checked((int)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = checked((long)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualResult = checked((nint)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualResult = checked((sbyte)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToSaturating{TOther}(TSelf, out TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertToSaturating<TOther>(nuint value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = (Half)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = (value >= (nuint)short.MaxValue) ? short.MaxValue : (short)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualResult = (value >= int.MaxValue) ? int.MaxValue : (int)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = (value >= long.MaxValue) ? long.MaxValue : (long)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualResult = (value >= (nuint)nint.MaxValue) ? nint.MaxValue : (nint)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualResult = (value >= (nuint)sbyte.MaxValue) ? sbyte.MaxValue : (sbyte)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToTruncating{TOther}(TSelf, out TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<nuint>.TryConvertToTruncating<TOther>(nuint value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `nuint` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = (Half)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = (short)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualResult = (int)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = (long)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualResult = (nint)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualResult = (sbyte)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }
}
