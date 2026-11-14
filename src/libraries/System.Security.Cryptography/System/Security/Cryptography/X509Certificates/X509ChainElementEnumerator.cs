// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;
using System.Collections.Generic;

namespace System.Security.Cryptography.X509Certificates
{
    public sealed class X509ChainElementEnumerator : IEnumerator, IEnumerator<X509ChainElement>
    {
        private readonly X509ChainElementCollection _chainElements;
        private int _current;

        internal X509ChainElementEnumerator(X509ChainElementCollection chainElements)
        {
            _chainElements = chainElements;
            _current = -1;
        }

        public X509ChainElement Current
        {
            get
            {
                return _chainElements[_current];
            }
        }

        object IEnumerator.Current
        {
            get
            {
                return Current;
            }
        }

        void IDisposable.Dispose()
        {
        }

        public bool MoveNext()
        {
            if (_current == _chainElements.Count - 1)
                return false;
            _current++;
            return true;
        }

        public void Reset()
        {
            _current = -1;
        }
    }
}
