// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Security.Authentication.ExtendedProtection
{
    // These should match the native SEC_ATTR_*_BINDINGS defines
    public enum ChannelBindingKind
    {
        Unknown = 0,
        Unique = 0x19,
        Endpoint = 0x1A
    }
}
