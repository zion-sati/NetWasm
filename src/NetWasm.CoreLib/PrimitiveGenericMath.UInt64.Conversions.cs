// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// UInt64 generic conversion contracts are retained from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 474ed963699fc29809015392738a15231aaae2de).

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

public partial struct UInt64
{
    /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        ulong result;

        if (typeof(TOther) == typeof(ulong))
        {
            result = (ulong)(object)value;
        }
        else if (!TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        ulong result;

        if (typeof(TOther) == typeof(ulong))
        {
            result = (ulong)(object)value;
        }
        else if (!TryConvertFromSaturating(value, out result) && !TOther.TryConvertToSaturating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        ulong result;

        if (typeof(TOther) == typeof(ulong))
        {
            result = (ulong)(object)value;
        }
        else if (!TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ulong>.TryConvertFromChecked<TOther>(TOther value, out ulong result) => TryConvertFromChecked(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromChecked<TOther>(TOther value, out ulong result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            result = checked((ulong)actualValue);
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
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = checked((ulong)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = actualValue;
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
    static bool INumberBase<ulong>.TryConvertFromSaturating<TOther>(TOther value, out ulong result) => TryConvertFromSaturating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromSaturating<TOther>(TOther value, out ulong result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            result = (actualValue >= MaxValue) ? MaxValue :
                     (actualValue <= MinValue) ? MinValue : (ulong)actualValue;
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
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (ulong)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = actualValue;
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
    static bool INumberBase<ulong>.TryConvertFromTruncating<TOther>(TOther value, out ulong result) => TryConvertFromTruncating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromTruncating<TOther>(TOther value, out ulong result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            result = (actualValue >= MaxValue) ? MaxValue :
                     (actualValue <= MinValue) ? MinValue : (ulong)actualValue;
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
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (ulong)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = actualValue;
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
    static bool INumberBase<ulong>.TryConvertToChecked<TOther>(ulong value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            Int128 actualResult = checked((Int128)value);
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
    static bool INumberBase<ulong>.TryConvertToSaturating<TOther>(ulong value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            short actualResult = (value >= (ulong)short.MaxValue) ? short.MaxValue : (short)value;
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
            nint actualResult = (value >= (ulong)nint.MaxValue) ? nint.MaxValue : (nint)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualResult = (value >= (ulong)sbyte.MaxValue) ? sbyte.MaxValue : (sbyte)value;
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
    static bool INumberBase<ulong>.TryConvertToTruncating<TOther>(ulong value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `ulong` will handle the other unsigned types and
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
            Int128 actualResult = (Int128)value;
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
