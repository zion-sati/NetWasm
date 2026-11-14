// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.NetworkInformation
{
    // See SCOPE_LEVEL
    public enum ScopeLevel
    {
        None = 0,
        Interface = 1,
        Link = 2,
        Subnet = 3,
        Admin = 4,
        Site = 5,
        Organization = 8,
        Global = 14,
    }
}
