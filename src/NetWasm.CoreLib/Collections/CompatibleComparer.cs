// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib CompatibleComparer.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// This comparer is retained because it is part of Hashtable's ordinary legacy
// comparer constructor surface; formatter serialization is not part of this port.

namespace System.Collections
{
    internal sealed class CompatibleComparer : IEqualityComparer
    {
        private readonly IHashCodeProvider? _hashCodeProvider;
        private readonly IComparer? _comparer;

        internal CompatibleComparer(IHashCodeProvider? hashCodeProvider, IComparer? comparer)
        {
            _hashCodeProvider = hashCodeProvider;
            _comparer = comparer;
        }

        internal IHashCodeProvider? HashCodeProvider => _hashCodeProvider;

        internal IComparer? Comparer => _comparer;

        public new bool Equals(object? first, object? second) => Compare(first, second) == 0;

        public int Compare(object? first, object? second)
        {
            if (first == second)
                return 0;
            if (first == null)
                return -1;
            if (second == null)
                return 1;

            if (_comparer != null)
                return _comparer.Compare(first, second);

            if (first is IComparable comparable)
                return comparable.CompareTo(second);

            throw new ArgumentException("At least one object must implement IComparable.");
        }

        public int GetHashCode(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return _hashCodeProvider?.GetHashCode(obj) ?? obj.GetHashCode();
        }
    }
}
