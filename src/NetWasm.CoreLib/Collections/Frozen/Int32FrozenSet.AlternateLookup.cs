// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
// Adapted from dotnet/runtime System.Collections.Immutable FrozenSet implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Collections.Frozen
{
    internal sealed partial class Int32FrozenSet
    {
        /// <inheritdoc/>
        private protected override AlternateLookupDelegate<TAlternate> GetAlternateLookupDelegate<TAlternate>()
            => AlternateLookupDelegateHolder<TAlternate>.Instance;

        private static class AlternateLookupDelegateHolder<TAlternate>
where TAlternate : allows ref struct
        {
            /// <summary>
            /// Invokes <see cref="FindItemIndexAlternate{TAlternate}(TAlternate)"/>
            /// on instances known to be of type <see cref="Int32FrozenSet"/>.
            /// </summary>
            public static readonly AlternateLookupDelegate<TAlternate> Instance = (set, item)
                => ((Int32FrozenSet)set).FindItemIndexAlternate(item);
        }

        /// <inheritdoc cref="FindItemIndex(int)" />
        private int FindItemIndexAlternate<TAlternate>(TAlternate item)
where TAlternate : allows ref struct
        {
            var comparer = GetAlternateEqualityComparer<TAlternate>();

            _hashTable.FindMatchingEntries(comparer.GetHashCode(item), out int index, out int endIndex);

            int[] hashCodes = _hashTable.HashCodes;
            while (index <= endIndex)
            {
                if (comparer.Equals(item, hashCodes[index]))
                {
                    return index;
                }

                index++;
            }

            return -1;
        }
    }
}
