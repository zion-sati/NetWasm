// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Adapted from dotnet/runtime System.Collections.Immutable FrozenSet implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Frozen
{
    /// <summary>Provides an empty <see cref="FrozenSet{T}"/> to use when there are zero values to be stored.</summary>
    internal sealed class EmptyFrozenSet<T> : FrozenSet<T>
    {
        internal EmptyFrozenSet(IEqualityComparer<T> comparer) : base(comparer) { }

        /// <inheritdoc />
        private protected override T[] ItemsCore => Array.Empty<T>();

        /// <inheritdoc />
        private protected override int CountCore => 0;

        /// <inheritdoc />
        private protected override int FindItemIndex(T item) => -1;

        /// <inheritdoc />
        private protected override Enumerator GetEnumeratorCore() => new Enumerator(Array.Empty<T>());

        /// <inheritdoc />
        private protected override bool IsProperSubsetOfCore(IEnumerable<T> other) => !OtherIsEmpty(other);

        /// <inheritdoc />
        private protected override bool IsProperSupersetOfCore(IEnumerable<T> other) => false;

        /// <inheritdoc />
        private protected override bool IsSubsetOfCore(IEnumerable<T> other) => true;

        /// <inheritdoc />
        private protected override bool IsSupersetOfCore(IEnumerable<T> other) => OtherIsEmpty(other);

        /// <inheritdoc />
        private protected override bool OverlapsCore(IEnumerable<T> other) => false;

        /// <inheritdoc />
        private protected override bool SetEqualsCore(IEnumerable<T> other) => OtherIsEmpty(other);

        private static bool OtherIsEmpty(IEnumerable<T> other)
        {
            if (other is IReadOnlyCollection<T> s)
            {
                return s.Count == 0;
            }

            using var enumerator = other.GetEnumerator();
            return !enumerator.MoveNext();
        }
    }
}
