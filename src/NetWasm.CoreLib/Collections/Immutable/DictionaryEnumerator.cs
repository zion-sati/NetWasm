// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Immutable
{
    internal sealed class DictionaryEnumerator<TKey, TValue> : IDictionaryEnumerator where TKey : notnull
    {
        private readonly IEnumerator<KeyValuePair<TKey, TValue>> _inner;

        internal DictionaryEnumerator(IEnumerator<KeyValuePair<TKey, TValue>> inner)
        {
            Requires.NotNull(inner, nameof(inner));

            _inner = inner;
        }

        public DictionaryEntry Entry
        {
            get { return new DictionaryEntry(_inner.Current.Key, _inner.Current.Value); }
        }

        public object Key
        {
            get { return _inner.Current.Key; }
        }

        public object? Value
        {
            get { return _inner.Current.Value; }
        }

        public object Current
        {
            get { return this.Entry; }
        }

        public bool MoveNext()
        {
            return _inner.MoveNext();
        }

        public void Reset()
        {
            _inner.Reset();
        }
    }
}

