// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.Diagnostics;
using System.Security.Cryptography;

namespace System.Security.Cryptography
{
    public enum KeyNumber
    {
        //These are identifiers for the private keys from the key container
        Exchange = 1,
        Signature = 2
    }
}
