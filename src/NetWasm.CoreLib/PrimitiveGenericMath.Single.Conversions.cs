// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Single generic conversion contracts are retained from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 474ed963699fc29809015392738a15231aaae2de).

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

public partial struct Single
{
    /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        float result;

        if (typeof(TOther) == typeof(float))
        {
            result = (float)(object)value;
        }
        else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToChecked(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        float result;

        if (typeof(TOther) == typeof(float))
        {
            result = (float)(object)value;
        }
        else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToSaturating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        float result;

        if (typeof(TOther) == typeof(float))
        {
            result = (float)(object)value;
        }
        else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToTruncating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<float>.TryConvertFromChecked<TOther>(TOther value, out float result)
    {
        return TryConvertFrom(value, out result);
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromSaturating{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<float>.TryConvertFromSaturating<TOther>(TOther value, out float result)
    {
        return TryConvertFrom(value, out result);
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromTruncating{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<float>.TryConvertFromTruncating<TOther>(TOther value, out float result)
    {
        return TryConvertFrom(value, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFrom<TOther>(TOther value, out float result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `float` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualValue = (double)(object)value;
            result = (float)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualValue = (Half)(object)value;
            result = (float)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualValue = (short)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualValue = (Int128)(object)value;
            result = (float)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualValue = (nint)(object)value;
            result = actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(sbyte))
        {
            sbyte actualValue = (sbyte)(object)value;
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
    static bool INumberBase<float>.TryConvertToChecked<TOther>(float value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `float` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types.

        if (typeof(TOther) == typeof(byte))
        {
            byte actualResult = checked((byte)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualResult = checked((char)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = checked((decimal)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualResult = checked((ushort)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualResult = checked((uint)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualResult = checked((ulong)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualResult = checked((UInt128)value);
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualResult = checked((nuint)value);
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
    static bool INumberBase<float>.TryConvertToSaturating<TOther>(float value, [MaybeNullWhen(false)] out TOther result)
    {
        return TryConvertTo(value, out result);
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToTruncating{TOther}(TSelf, out TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<float>.TryConvertToTruncating<TOther>(float value, [MaybeNullWhen(false)] out TOther result)
    {
        return TryConvertTo(value, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertTo<TOther>(float value, [MaybeNullWhen(false)] out TOther result)
        where TOther : INumberBase<TOther>
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `float` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types.

        if (typeof(TOther) == typeof(byte))
        {
#if MONO
                byte actualResult = (value >= byte.MaxValue) ? byte.MaxValue :
                                    (value <= byte.MinValue) ? byte.MinValue : (byte)value;
#else
            byte actualResult = (byte)value;
#endif
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
#if MONO
                char actualResult = (value >= char.MaxValue) ? char.MaxValue :
                                    (value <= char.MinValue) ? char.MinValue : (char)value;
#else
            char actualResult = (char)value;
#endif
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = (value >= +79228162514264337593543950336.0f) ? decimal.MaxValue :
                                   (value <= -79228162514264337593543950336.0f) ? decimal.MinValue :
                                   IsNaN(value) ? 0.0m : (decimal)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
#if MONO
                ushort actualResult = (value >= ushort.MaxValue) ? ushort.MaxValue :
                                      (value <= ushort.MinValue) ? ushort.MinValue : (ushort)value;
#else
            ushort actualResult = (ushort)value;
#endif
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
#if MONO
                uint actualResult = (value >= uint.MaxValue) ? uint.MaxValue :
                                    (value <= uint.MinValue) ? uint.MinValue : (uint)value;
#else
            uint actualResult = (uint)value;
#endif
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
#if MONO
                ulong actualResult = (value >= ulong.MaxValue) ? ulong.MaxValue :
                                     (value <= ulong.MinValue) ? ulong.MinValue :
                                     IsNaN(value) ? 0 : (ulong)value;
#else
            ulong actualResult = (ulong)value;
#endif
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualResult = (UInt128)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
#if MONO
                nuint actualResult = (value >= nuint.MaxValue) ? nuint.MaxValue :
                                     (value <= nuint.MinValue) ? nuint.MinValue : (nuint)value;
#else
            nuint actualResult = (nuint)value;
#endif
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
