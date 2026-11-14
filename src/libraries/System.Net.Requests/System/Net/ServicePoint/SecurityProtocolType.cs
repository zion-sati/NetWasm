// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Data contracts from NETStandard.Library.Ref 2.1.0.

namespace System.Net;

[System.Flags]
public enum SecurityProtocolType
{
    Ssl3 = 48,
    SystemDefault = 0,
    Tls = 192,
    Tls11 = 768,
    Tls12 = 3072,
}
