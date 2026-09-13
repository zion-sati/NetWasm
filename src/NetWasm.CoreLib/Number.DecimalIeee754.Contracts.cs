// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// This internal contract connects Decimal32/64/128 to the shared Number helpers.

namespace System;

internal interface IDecimalIeee754ParseAndFormatInfo<TSelf, TValue>
    where TSelf : unmanaged, IDecimalIeee754ParseAndFormatInfo<TSelf, TValue>
    where TValue : unmanaged, Numerics.IBinaryInteger<TValue>
{
    static abstract int Precision { get; }
    static abstract int BufferLength { get; }
    static abstract int MaxExponent { get; }
    static abstract int MinExponent { get; }
    static virtual int MaxAdjustedExponent => TSelf.MaxExponent - TSelf.Precision + 1;
    static virtual int MinAdjustedExponent => TSelf.MinExponent - TSelf.Precision + 1;
    static abstract int ExponentBias { get; }
    static abstract TValue PositiveInfinity { get; }
    static abstract TValue NegativeInfinity { get; }
    static abstract TValue NaN { get; }
    static abstract TValue Zero { get; }
    static abstract TValue MaxSignificand { get; }
    static abstract TValue NumberToSignificand(ref Number.NumberBuffer number, int digits);
    static abstract string ToDecStr(TValue significand);
    static abstract int ConvertToExponent(TValue value);
    static abstract TValue Power10(int exponent);
    static abstract (TValue Quotient, TValue Remainder) DivRemPow10(TValue value, int exponent);
    static abstract TSelf Construct(TValue value);
    static abstract int CountDigits(TValue significand);
    static abstract int NumberBitsSignificand { get; }
    static abstract TValue NaNMask { get; }
    static abstract TValue SNaNMask { get; }
    static abstract TValue NaNPayloadMask { get; }
    static abstract TValue SignMask { get; }
    static abstract TValue G0G1Mask { get; }
    static abstract TValue G0ToGwPlus1ExponentMask { get; }
    static abstract TValue G2ToGwPlus3ExponentMask { get; }
    static abstract TValue GwPlus2ToGwPlus4SignificandMask { get; }
    static abstract TValue GwPlus4SignificandMask { get; }
    static abstract TValue MostSignificantBitOfSignificandMask { get; }
    static abstract bool IsNaN(TValue decimalBits);
    static abstract bool IsFinite(TValue decimalBits);
    static abstract bool IsInfinity(TValue decimalBits);
    static abstract bool IsPositiveInfinity(TValue decimalBits);
    static abstract bool IsNegativeInfinity(TValue decimalBits);
    static abstract bool IsNegative(TValue decimalBits);
    static abstract TValue EncodeExponentToG0ThroughGwPlus1(uint biasedExponent);
    static abstract TValue EncodeExponentToG2ThroughGwPlus3(uint biasedExponent);
}
