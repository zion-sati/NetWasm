// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.Diagnostics;

namespace System.Security.Cryptography.X509Certificates
{
    public enum StoreLocation
    {
        CurrentUser = 0x01,
        LocalMachine = 0x02,
    }
}
