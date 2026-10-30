// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib DictionaryEntry.cs at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace System.Collections
{
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public struct DictionaryEntry
    {
        private object _key;
        private object? _value;

        public DictionaryEntry(object key, object? value)
        {
            _key = key;
            _value = value;
        }

        public object Key
        {
            get => _key;
            set => _key = value;
        }

        public object? Value
        {
            get => _value;
            set => _value = value;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out object key, out object? value)
        {
            key = Key;
            value = Value;
        }

        public override string ToString() =>
            KeyValuePair.PairToString(_key, _value);
    }
}
