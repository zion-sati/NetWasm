// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

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
