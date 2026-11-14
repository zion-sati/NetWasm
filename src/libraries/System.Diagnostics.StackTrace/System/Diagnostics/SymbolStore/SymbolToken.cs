// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;

namespace System.Diagnostics.SymbolStore
{
    public readonly struct SymbolToken : IEquatable<SymbolToken>
    {
        private readonly int _token;

        public SymbolToken(int val)
        {
            _token = val;
        }

        public int GetToken() => _token;

        public override int GetHashCode() => _token;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is SymbolToken)
                return Equals((SymbolToken)obj);
            else
                return false;
        }

        public bool Equals(SymbolToken obj) => obj._token == _token;

        public static bool operator ==(SymbolToken a, SymbolToken b) => a.Equals(b);

        public static bool operator !=(SymbolToken a, SymbolToken b) => !(a == b);
    }
}
