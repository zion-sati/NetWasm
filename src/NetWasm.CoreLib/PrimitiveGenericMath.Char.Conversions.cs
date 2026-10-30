// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Char generic conversion contracts are retained from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 474ed963699fc29809015392738a15231aaae2de).

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

public partial struct Char
{
    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<char>.TryConvertFromChecked<TOther>(TOther value, out char result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = checked((char)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = checked((char)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = checked((char)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = checked((char)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = checked((char)actualValue);
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
    static bool INumberBase<char>.TryConvertFromSaturating<TOther>(TOther value, out char result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
                     (actualValue <= MinValue) ? MinValue : (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (char)actualValue;
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
    static bool INumberBase<char>.TryConvertFromTruncating<TOther>(TOther value, out char result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualValue = (byte)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
                     (actualValue <= MinValue) ? MinValue : (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (char)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = (char)actualValue;
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
    static bool INumberBase<char>.TryConvertToChecked<TOther>(char value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
        // `ConvertTo` will handle the unsigned types

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
            int actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = value;
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
            nint actualResult = value;
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
    static bool INumberBase<char>.TryConvertToSaturating<TOther>(char value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
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
            short actualResult = (value >= short.MaxValue) ? short.MaxValue : (short)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = value;
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
            nint actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualResult = (value >= sbyte.MaxValue) ? sbyte.MaxValue : (sbyte)value;
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
    static bool INumberBase<char>.TryConvertToTruncating<TOther>(char value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `char` will handle the other unsigned types and
        // `ConvertTo` will handle the unsigned types

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
            int actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualResult = value;
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
            nint actualResult = value;
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
