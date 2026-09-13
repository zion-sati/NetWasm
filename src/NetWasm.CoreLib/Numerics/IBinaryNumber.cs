// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Numerics
{
    /// <summary>Defines a number that is represented in a base-2 format.</summary>
    /// <typeparam name="TSelf">The type that implements the interface.</typeparam>
    public interface IBinaryNumber<TSelf>
        : IComparable,
          IComparable<TSelf>,
          IEquatable<TSelf>,
          IFormattable,
          IParsable<TSelf>,
          ISpanFormattable,
          ISpanParsable<TSelf>,
          IAdditionOperators<TSelf, TSelf, TSelf>,
          IAdditiveIdentity<TSelf, TSelf>,
          IBitwiseOperators<TSelf, TSelf, TSelf>,
          IComparisonOperators<TSelf, TSelf, bool>,
          IDecrementOperators<TSelf>,
          IDivisionOperators<TSelf, TSelf, TSelf>,
          IEqualityOperators<TSelf, TSelf, bool>,
          IIncrementOperators<TSelf>,
          IModulusOperators<TSelf, TSelf, TSelf>,
          IMultiplicativeIdentity<TSelf, TSelf>,
          IMultiplyOperators<TSelf, TSelf, TSelf>,
          INumber<TSelf>,
          INumberBase<TSelf>,
          ISubtractionOperators<TSelf, TSelf, TSelf>,
          IUnaryNegationOperators<TSelf, TSelf>,
          IUnaryPlusOperators<TSelf, TSelf>
        where TSelf : IBinaryNumber<TSelf>?
    {
        /// <summary>Gets an instance of the binary type in which all bits are set.</summary>
        static virtual TSelf AllBitsSet { get => ~TSelf.Zero; }

        /// <summary>Determines if a value is a power of two.</summary>
        /// <param name="value">The value to be checked.</param>
        /// <returns><c>true</c> if <paramref name="value" /> is a power of two; otherwise, <c>false</c>.</returns>
        static abstract bool IsPow2(TSelf value);

        /// <summary>Computes the log2 of a value.</summary>
        /// <param name="value">The value whose log2 is to be computed.</param>
        /// <returns>The log2 of <paramref name="value" />.</returns>
        static abstract TSelf Log2(TSelf value);
    }
}
