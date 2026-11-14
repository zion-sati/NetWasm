// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;

namespace System.IO.IsolatedStorage
{
    [Flags]
    public enum IsolatedStorageScope
    {
        None = 0,
        User = 1,
        Domain = 2,
        Assembly = 4,
        Roaming = 8,
        Machine = 16,
        Application = 32
    }
}
