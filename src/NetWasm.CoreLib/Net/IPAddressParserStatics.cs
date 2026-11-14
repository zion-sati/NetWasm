// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net
{
    internal static class IPAddressParserStatics
    {
        public const int IPv4AddressBytes = 4;
        public const int IPv6AddressBytes = 16;
        public const int IPv6AddressShorts = IPv6AddressBytes / 2;
    }
}
