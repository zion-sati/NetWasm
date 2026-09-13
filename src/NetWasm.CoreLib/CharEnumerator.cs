// Portions derived from dotnet/runtime System.Private.CoreLib CharEnumerator.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace System
{
    /// <summary>Enumerates the UTF-16 code units in a string.</summary>
    public sealed class CharEnumerator : IEnumerator<char>, IEnumerator, ICloneable, IDisposable
    {
        private string? _value;
        private int _index = -1;

        internal CharEnumerator(string value) => _value = value;

        private CharEnumerator(string? value, int index)
        {
            _value = value;
            _index = index;
        }

        public object Clone() => new CharEnumerator(_value, _index);

        public bool MoveNext()
        {
            var value = _value ?? throw new ObjectDisposedException(nameof(CharEnumerator));
            var next = _index + 1;
            if (next < value.Length)
            {
                _index = next;
                return true;
            }
            _index = value.Length;
            return false;
        }

        public char Current
        {
            get
            {
                var value = _value ?? throw new ObjectDisposedException(nameof(CharEnumerator));
                if ((uint)_index >= (uint)value.Length)
                {
                    throw new InvalidOperationException();
                }
                return value[_index];
            }
        }

        object IEnumerator.Current => Current;

        public void Reset()
        {
            if (_value is null)
            {
                throw new ObjectDisposedException(nameof(CharEnumerator));
            }
            _index = -1;
        }

        public void Dispose() => _value = null;
    }
}
