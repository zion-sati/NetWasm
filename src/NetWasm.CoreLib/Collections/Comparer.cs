// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de:
// src/libraries/System.Private.CoreLib/src/System/Collections/Comparer.cs.
// NetWasm adaptation: CoreLib has no ISerializable yet, so Comparer does not implement it and its
// serialization constructor and GetObjectData are left out.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Collections
{
    /// <summary>
    /// Compares two objects for equivalence, where string comparisons are case-sensitive.
    /// </summary>
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed class Comparer : IComparer
    {
        private readonly CompareInfo _compareInfo;

        public static readonly Comparer Default = new Comparer(CultureInfo.CurrentCulture);
        public static readonly Comparer DefaultInvariant = new Comparer(CultureInfo.InvariantCulture);

        public Comparer(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            _compareInfo = culture.CompareInfo;
        }

        // Compares two Objects by calling CompareTo.
        // If a == b, 0 is returned.
        // If a implements IComparable, a.CompareTo(b) is returned.
        // If a doesn't implement IComparable and b does, -(b.CompareTo(a)) is returned.
        // Otherwise an exception is thrown.
        //
        public int Compare(object? a, object? b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a is string sa && b is string sb)
                return _compareInfo.Compare(sa, sb);

            if (a is IComparable ia)
                return ia.CompareTo(b);

            if (b is IComparable ib)
                return -ib.CompareTo(a);

            throw new ArgumentException(SR.Argument_ImplementIComparable);
        }
    }
}
