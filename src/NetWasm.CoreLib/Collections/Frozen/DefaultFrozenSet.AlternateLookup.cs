// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Adapted from dotnet/runtime System.Collections.Immutable FrozenSet implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Frozen
{
    internal sealed partial class DefaultFrozenSet<T>
    {
        /// <inheritdoc/>
        private protected override AlternateLookupDelegate<TAlternate> GetAlternateLookupDelegate<TAlternate>()
            => AlternateLookupDelegateHolder<TAlternate>.Instance;

        private static class AlternateLookupDelegateHolder<TAlternate>
where TAlternate : allows ref struct
        {
            /// <summary>
            /// Invokes <see cref="FindItemIndexAlternate{TAlternate}(TAlternate)"/>
            /// on instances known to be of type <see cref="DefaultFrozenSet{TValue}"/>.
            /// </summary>
            public static readonly AlternateLookupDelegate<TAlternate> Instance = (set, item)
                => ((DefaultFrozenSet<T>)set).FindItemIndexAlternate(item);
        }

        /// <inheritdoc cref="FindItemIndex(T)" />
        private int FindItemIndexAlternate<TAlternate>(TAlternate item)
where TAlternate : allows ref struct
        {
            IAlternateEqualityComparer<TAlternate, T> comparer = GetAlternateEqualityComparer<TAlternate>();

            int hashCode = item is null ? 0 : comparer.GetHashCode(item);
            _hashTable.FindMatchingEntries(hashCode, out int index, out int endIndex);

            while (index <= endIndex)
            {
                if (hashCode == _hashTable.HashCodes[index] && comparer.Equals(item, _items[index]))
                {
                    return index;
                }

                index++;
            }

            return -1;
        }
    }
}
