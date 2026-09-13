// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.


using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.Frozen
{
    /// <summary>Provides a frozen dictionary to use when the key is a value type, the default comparer is used, and the item count is small.</summary>
    internal sealed class SmallValueTypeDefaultComparerFrozenDictionary<TKey, TValue> : FrozenDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly TKey[] _keys;
        private readonly TValue[] _values;

        internal SmallValueTypeDefaultComparerFrozenDictionary(Dictionary<TKey, TValue> source) : base(EqualityComparer<TKey>.Default)
        {
            Debug.Assert(default(TKey) is not null);

            Debug.Assert(source.Count != 0);
            Debug.Assert(ReferenceEquals(source.Comparer, EqualityComparer<TKey>.Default));

            _keys = new TKey[source.Count];
            _values = new TValue[source.Count];
            source.Keys.CopyTo(_keys, 0);
            source.Values.CopyTo(_values, 0);
        }

        private protected override TKey[] KeysCore => _keys;
        private protected override TValue[] ValuesCore => _values;
        private protected override Enumerator GetEnumeratorCore() => new Enumerator(_keys, _values);
        private protected override int CountCore => _keys.Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected override ref readonly TValue GetValueRefOrNullRefCore(TKey key)
        {
            TKey[] keys = _keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (EqualityComparer<TKey>.Default.Equals(keys[i], key))
                {
                    return ref _values[i];
                }
            }

            return ref Unsafe.NullRef<TValue>();
        }
    }
}
