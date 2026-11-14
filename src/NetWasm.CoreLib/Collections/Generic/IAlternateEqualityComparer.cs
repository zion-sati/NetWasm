// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib
// IAlternateEqualityComparer.cs at commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Collections.Generic
{
    public interface IAlternateEqualityComparer<in TAlternate, T>
        where TAlternate : allows ref struct
        where T : allows ref struct
    {
        bool Equals(TAlternate alternate, T other);
        int GetHashCode(TAlternate alternate);
        T Create(TAlternate alternate);
    }
}
