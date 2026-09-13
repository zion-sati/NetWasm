// Portions derived from dotnet/runtime System.Private.CoreLib DictionaryEntry at
// commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections
{
    public struct DictionaryEntry
    {
        private object _key;
        private object? _value;

        public DictionaryEntry(object key, object? value)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _value = value;
        }

        public object Key
        {
            get => _key;
            set => _key = value ?? throw new ArgumentNullException(nameof(value));
        }

        public object? Value
        {
            get => _value;
            set => _value = value;
        }

        public void Deconstruct(out object key, out object? value)
        {
            key = Key;
            value = Value;
        }
    }
}
