// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Adapted from dotnet/runtime System.Collections.Immutable FrozenSet implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Collections.Frozen
{
    /// <summary>Provides a frozen set to use when the item is a value type, the default comparer is used, and the item count is small.</summary>
    internal sealed class SmallValueTypeDefaultComparerFrozenSet<T> : FrozenSetInternalBase<T, SmallValueTypeDefaultComparerFrozenSet<T>.GSW>
    {
        private readonly T[] _items;

        internal SmallValueTypeDefaultComparerFrozenSet(HashSet<T> source) : base(EqualityComparer<T>.Default)
        {
            Debug.Assert(default(T) is not null);

            Debug.Assert(source.Count != 0);
            Debug.Assert(ReferenceEquals(source.Comparer, EqualityComparer<T>.Default));

            _items = new T[source.Count];
            source.CopyTo(_items);
        }

        private protected override T[] ItemsCore => _items;
        private protected override Enumerator GetEnumeratorCore() => new Enumerator(_items);
        private protected override int CountCore => _items.Length;

        private protected override int FindItemIndex(T item)
        {
            T[] items = _items;
            for (int i = 0; i < items.Length; i++)
            {
                if (EqualityComparer<T>.Default.Equals(item, items[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        internal struct GSW : IGenericSpecializedWrapper
        {
            private SmallValueTypeDefaultComparerFrozenSet<T> _set;
            public void Store(FrozenSet<T> set) => _set = (SmallValueTypeDefaultComparerFrozenSet<T>)set;

            public int Count => _set.Count;
            public IEqualityComparer<T> Comparer => _set.Comparer;
            public int FindItemIndex(T item) => _set.FindItemIndex(item);
            public Enumerator GetEnumerator() => _set.GetEnumerator();
        }
    }
}
