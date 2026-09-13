// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Adapted from dotnet/runtime System.Collections.Immutable FrozenSet implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Frozen
{
    internal sealed partial class SmallFrozenSet<T>
    {
        /// <inheritdoc/>
        private protected override AlternateLookupDelegate<TAlternateKey> GetAlternateLookupDelegate<TAlternateKey>()
            => AlternateLookupDelegateHolder<TAlternateKey>.Instance;

        private static class AlternateLookupDelegateHolder<TAlternateKey>
where TAlternateKey : allows ref struct
        {
            /// <summary>
            /// Invokes <see cref="FindItemIndexAlternate{TAlternate}(TAlternate)"/>
            /// on instances known to be of type <see cref="SmallFrozenSet{T}"/>.
            /// </summary>
            public static readonly AlternateLookupDelegate<TAlternateKey> Instance = (set, item)
                => ((SmallFrozenSet<T>)set).FindItemIndexAlternate(item);
        }

        /// <inheritdoc cref="FindItemIndex(T)" />
        private int FindItemIndexAlternate<TAlternate>(TAlternate item)
where TAlternate : allows ref struct
        {
            IAlternateEqualityComparer<TAlternate, T> comparer = GetAlternateEqualityComparer<TAlternate>();

            T[] items = _items;
            for (int i = 0; i < items.Length; i++)
            {
                if (comparer.Equals(item, items[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
