// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib CompatibleComparer.cs at commit
// 474ed963699fc29809015392738a15231aaae2de.
// This comparer is retained because it is part of Hashtable's ordinary legacy
// comparer constructor surface; formatter serialization is not part of this port.

namespace System.Collections
{
    internal sealed class CompatibleComparer : IEqualityComparer
    {
        private readonly IHashCodeProvider? _hcp;
        private readonly IComparer? _comparer;

        internal CompatibleComparer(IHashCodeProvider? hashCodeProvider, IComparer? comparer)
        {
            _hcp = hashCodeProvider;
            _comparer = comparer;
        }

        internal IHashCodeProvider? HashCodeProvider => _hcp;

        internal IComparer? Comparer => _comparer;

        public new bool Equals(object? a, object? b) => Compare(a, b) == 0;

        public int Compare(object? a, object? b)
        {
            if (a == b)
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            if (_comparer != null)
            {
                return _comparer.Compare(a, b);
            }

            if (a is IComparable ia)
            {
                return ia.CompareTo(b);
            }

            throw new ArgumentException(SR.Argument_ImplementIComparable);
        }

        public int GetHashCode(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return _hcp != null ?
                _hcp.GetHashCode(obj) :
                obj.GetHashCode();
        }
    }
}
