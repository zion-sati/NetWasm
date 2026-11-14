// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Data contracts from NETStandard.Library.Ref 2.1.0.

namespace System.Net;

[System.Flags]
public enum AuthenticationSchemes
{
    Anonymous = 32768,
    Basic = 8,
    Digest = 1,
    IntegratedWindowsAuthentication = 6,
    Negotiate = 2,
    None = 0,
    Ntlm = 4,
}
