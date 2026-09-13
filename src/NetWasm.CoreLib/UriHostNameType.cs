// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    // Adapted from dotnet/runtime System.Private.Uri at commit
    // 811225a482702af7ecc35d817966bc70b88a3a23.
    public enum UriHostNameType
    {
        Unknown,
        Basic,
        Dns,
        IPv4,
        IPv6
    }
}
