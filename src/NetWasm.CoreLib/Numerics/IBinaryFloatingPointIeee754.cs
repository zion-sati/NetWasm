// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Numerics
{
    /// <summary>Defines an IEEE 754 floating-point type that is represented in a base-2 format.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IBinaryFloatingPointIeee754<TSelf>
        : IComparable,
          IComparable<TSelf>,
          IEquatable<TSelf>,
          IFormattable,
          IParsable<TSelf>,
          ISpanFormattable,
          ISpanParsable<TSelf>,
          IUtf8SpanFormattable,
          IUtf8SpanParsable<TSelf>,
          IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IBinaryNumber<TSelf>,
          IBitwiseOperators<TSelf, TSelf, TSelf>,
          IComparisonOperators<TSelf, TSelf, bool>,
          IDecrementOperators<TSelf>,
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IEqualityOperators<TSelf, TSelf, bool>,
          IExponentialFunctions<TSelf>,
          IFloatingPoint<TSelf>,
          IFloatingPointConstants<TSelf>,
          IFloatingPointIeee754<TSelf>,
          IHyperbolicFunctions<TSelf>,
          IIncrementOperators<TSelf>,
          ILogarithmicFunctions<TSelf>,
          IModulusOperators<TSelf, TSelf, TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplyOperators<TSelf, TSelf, TSelf>,
          INumber<TSelf>,
          INumberBase<TSelf>,
          IPowerFunctions<TSelf>,
          IRootFunctions<TSelf>,
          ISignedNumber<TSelf>,
          ISubtractionOperators<TSelf, TSelf, TSelf>,
          ITrigonometricFunctions<TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>,
          IUnaryPlusOperators<TSelf, TSelf>
        where TSelf : IBinaryFloatingPointIeee754<TSelf>?
    {
    }
}
