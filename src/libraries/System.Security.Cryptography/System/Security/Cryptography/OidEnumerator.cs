// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;

namespace System.Security.Cryptography
{
    public sealed class OidEnumerator : IEnumerator
    {
        internal OidEnumerator(OidCollection oids)
        {
            _oids = oids;
            _current = -1;
        }

        public Oid Current => _oids[_current];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_current >= _oids.Count - 1)
                return false;

            _current++;
            return true;
        }

        public void Reset()
        {
            _current = -1;
        }

        private readonly OidCollection _oids;
        private int _current;
    }
}
