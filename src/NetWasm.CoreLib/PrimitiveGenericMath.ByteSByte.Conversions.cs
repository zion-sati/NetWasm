// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Byte and SByte generic conversion contracts are retained from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 474ed963699fc29809015392738a15231aaae2de).

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

public partial struct Byte
{
    /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte CreateChecked<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        byte result;

        if (typeof(TOther) == typeof(byte))
        {
            result = (byte)(object)value;
        }
        else if (!TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte CreateSaturating<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        byte result;

        if (typeof(TOther) == typeof(byte))
        {
            result = (byte)(object)value;
        }
        else if (!TryConvertFromSaturating(value, out result) && !TOther.TryConvertToSaturating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte CreateTruncating<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        byte result;

        if (typeof(TOther) == typeof(byte))
        {
            result = (byte)(object)value;
        }
        else if (!TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<byte>.TryConvertFromChecked<TOther>(TOther value, out byte result) => TryConvertFromChecked(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromChecked<TOther>(TOther value, out byte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            if (!PrimitiveIntegralConversions.TryConvert(value, 8, signed: false, PrimitiveIntegralConversionKind.Checked, out var converted))
            {
                throw new OverflowException();
            }

            result = (byte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = checked((byte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = checked((byte)actualValue);
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
    static bool INumberBase<byte>.TryConvertFromSaturating<TOther>(TOther value, out byte result) => TryConvertFromSaturating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromSaturating<TOther>(TOther value, out byte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            _ = PrimitiveIntegralConversions.TryConvert(value, 8, signed: false, PrimitiveIntegralConversionKind.Saturating, out var converted);
            result = (byte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue : (byte)actualValue;
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
    static bool INumberBase<byte>.TryConvertFromTruncating<TOther>(TOther value, out byte result) => TryConvertFromTruncating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromTruncating<TOther>(TOther value, out byte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            _ = PrimitiveIntegralConversions.TryConvert(value, 8, signed: false, PrimitiveIntegralConversionKind.Truncating, out var converted);
            result = (byte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the signed types

        if (typeof(TOther) == typeof(char))
        {
            char actualValue = (char)(object)value;
            result = (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualValue = (decimal)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualValue = (ushort)(object)value;
            result = (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualValue = (uint)(object)value;
            result = (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualValue = (UInt128)(object)value;
            result = (byte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualValue = (nuint)(object)value;
            result = (byte)actualValue;
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
    static bool INumberBase<byte>.TryConvertToChecked<TOther>(byte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = value;
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
    static bool INumberBase<byte>.TryConvertToSaturating<TOther>(byte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = value;
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
    static bool INumberBase<byte>.TryConvertToTruncating<TOther>(byte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `byte` will handle the other unsigned types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualResult = value;
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

public partial struct SByte
{
    /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static sbyte CreateChecked<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        sbyte result;

        if (typeof(TOther) == typeof(sbyte))
        {
            result = (sbyte)(object)value;
        }
        else if (!TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static sbyte CreateSaturating<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        sbyte result;

        if (typeof(TOther) == typeof(sbyte))
        {
            result = (sbyte)(object)value;
        }
        else if (!TryConvertFromSaturating(value, out result) && !TOther.TryConvertToSaturating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static sbyte CreateTruncating<TOther>(TOther value)
    where TOther : INumberBase<TOther>
    {
        sbyte result;

        if (typeof(TOther) == typeof(sbyte))
        {
            result = (sbyte)(object)value;
        }
        else if (!TryConvertFromTruncating(value, out result) && !TOther.TryConvertToTruncating(value, out result))
        {
            ThrowHelper.ThrowNotSupportedException();
        }

        return result;
    }

    /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<sbyte>.TryConvertFromChecked<TOther>(TOther value, out sbyte result) => TryConvertFromChecked(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromChecked<TOther>(TOther value, out sbyte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            if (!PrimitiveIntegralConversions.TryConvert(value, 8, signed: true, PrimitiveIntegralConversionKind.Checked, out var converted))
            {
                throw new OverflowException();
            }

            result = (sbyte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualValue = (double)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualValue = (Half)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualValue = (short)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualValue = (Int128)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualValue = (nint)(object)value;
            result = checked((sbyte)actualValue);
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualValue = (float)(object)value;
            result = checked((sbyte)actualValue);
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
    static bool INumberBase<sbyte>.TryConvertFromSaturating<TOther>(TOther value, out sbyte result) => TryConvertFromSaturating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromSaturating<TOther>(TOther value, out sbyte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            _ = PrimitiveIntegralConversions.TryConvert(value, 8, signed: true, PrimitiveIntegralConversionKind.Saturating, out var converted);
            result = (sbyte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualValue = (double)(object)value;
#if MONO
    result = (actualValue >= MaxValue) ? MaxValue :
    (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
#else
            result = (sbyte)actualValue;
#endif
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualValue = (Half)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualValue = (short)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualValue = (Int128)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualValue = (nint)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualValue = (float)(object)value;
#if MONO
    result = (actualValue >= MaxValue) ? MaxValue :
    (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
#else
            result = (sbyte)actualValue;
#endif
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
    static bool INumberBase<sbyte>.TryConvertFromTruncating<TOther>(TOther value, out sbyte result) => TryConvertFromTruncating(value, out result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromTruncating<TOther>(TOther value, out sbyte result)
    where TOther : INumberBase<TOther>
    {
        if (PrimitiveIntegralConversions.IsBuiltInNumber<TOther>())
        {
            _ = PrimitiveIntegralConversions.TryConvert(value, 8, signed: true, PrimitiveIntegralConversionKind.Truncating, out var converted);
            result = (sbyte)converted;
            return true;
        }

        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(double))
        {
            double actualValue = (double)(object)value;
#if MONO
    result = (actualValue >= MaxValue) ? MaxValue :
    (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
#else
            result = (sbyte)actualValue;
#endif
            return true;
        }
        else if (typeof(TOther) == typeof(Half))
        {
            Half actualValue = (Half)(object)value;
            result = (actualValue >= MaxValue) ? MaxValue :
            (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(short))
        {
            short actualValue = (short)(object)value;
            result = (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(int))
        {
            int actualValue = (int)(object)value;
            result = (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(long))
        {
            long actualValue = (long)(object)value;
            result = (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(Int128))
        {
            Int128 actualValue = (Int128)(object)value;
            result = (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(nint))
        {
            nint actualValue = (nint)(object)value;
            result = (sbyte)actualValue;
            return true;
        }
        else if (typeof(TOther) == typeof(float))
        {
            float actualValue = (float)(object)value;
#if MONO
    result = (actualValue >= MaxValue) ? MaxValue :
    (actualValue <= MinValue) ? MinValue : (sbyte)actualValue;
#else
            result = (sbyte)actualValue;
#endif
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
    static bool INumberBase<sbyte>.TryConvertToChecked<TOther>(sbyte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

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
            decimal actualResult = value;
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
    static bool INumberBase<sbyte>.TryConvertToSaturating<TOther>(sbyte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualResult = (value <= 0) ? byte.MinValue : (byte)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualResult = (value <= 0) ? char.MinValue : (char)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualResult = (value <= 0) ? ushort.MinValue : (ushort)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualResult = (value <= 0) ? uint.MinValue : (uint)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualResult = (value <= 0) ? ulong.MinValue : (ulong)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(UInt128))
        {
            UInt128 actualResult = (value <= 0) ? UInt128.MinValue : (UInt128)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(nuint))
        {
            nuint actualResult = (value <= 0) ? 0 : (nuint)value;
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
    static bool INumberBase<sbyte>.TryConvertToTruncating<TOther>(sbyte value, [MaybeNullWhen(false)] out TOther result)
    {
        // In order to reduce overall code duplication and improve the inlinabilty of these
        // methods for the corelib types we have `ConvertFrom` handle the same sign and
        // `ConvertTo` handle the opposite sign. However, since there is an uneven split
        // between signed and unsigned types, the one that handles unsigned will also
        // handle `Decimal`.
        //
        // That is, `ConvertFrom` for `sbyte` will handle the other signed types and
        // `ConvertTo` will handle the unsigned types

        if (typeof(TOther) == typeof(byte))
        {
            byte actualResult = (byte)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(char))
        {
            char actualResult = (char)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(decimal))
        {
            decimal actualResult = value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ushort))
        {
            ushort actualResult = (ushort)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(uint))
        {
            uint actualResult = (uint)value;
            result = (TOther)(object)actualResult;
            return true;
        }
        else if (typeof(TOther) == typeof(ulong))
        {
            ulong actualResult = (ulong)value;
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
            nuint actualResult = (nuint)value;
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
