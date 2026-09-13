// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Numerics;

namespace System;

internal interface IBinaryIntegerParseAndFormatInfo<TSelf> :
    IBinaryInteger<TSelf>,
    IMinMaxValue<TSelf>
    where TSelf : unmanaged, IBinaryIntegerParseAndFormatInfo<TSelf>
{
    static abstract bool IsSigned { get; }
    static abstract int MaxDigitCount { get; }
    static abstract int MaxHexDigitCount { get; }
    static abstract TSelf MaxValueDiv10 { get; }
    static abstract string OverflowMessage { get; }
    static abstract bool IsGreaterThanAsUnsigned(TSelf left, TSelf right);
    static abstract TSelf MultiplyBy10(TSelf value);
    static abstract TSelf MultiplyBy16(TSelf value);
}

internal interface IBinaryFloatParseAndFormatInfo<TSelf> :
    IBinaryFloatingPointIeee754<TSelf>,
    IMinMaxValue<TSelf>
    where TSelf : unmanaged, IBinaryFloatParseAndFormatInfo<TSelf>
{
    static abstract int NumberBufferLength { get; }
    static abstract ulong ZeroBits { get; }
    static abstract ulong InfinityBits { get; }
    static abstract ulong NormalMantissaMask { get; }
    static abstract ulong DenormalMantissaMask { get; }
    static abstract int MinBinaryExponent { get; }
    static abstract int MaxBinaryExponent { get; }
    static abstract int MinDecimalExponent { get; }
    static abstract int MaxDecimalExponent { get; }
    static abstract int ExponentBias { get; }
    static abstract ushort ExponentBits { get; }
    static abstract int OverflowDecimalExponent { get; }
    static abstract int InfinityExponent { get; }
    static abstract ushort NormalMantissaBits { get; }
    static abstract ushort DenormalMantissaBits { get; }
    static abstract int MinFastFloatDecimalExponent { get; }
    static abstract int MaxFastFloatDecimalExponent { get; }
    static abstract int MinExponentRoundToEven { get; }
    static abstract int MaxExponentRoundToEven { get; }
    static abstract int MaxExponentFastPath { get; }
    static abstract ulong MaxMantissaFastPath { get; }
    static abstract TSelf BitsToFloat(ulong bits);
    static abstract ulong FloatToBits(TSelf value);
    static abstract int MaxRoundTripDigits { get; }
    static abstract int MaxPrecisionCustomFormat { get; }
}
