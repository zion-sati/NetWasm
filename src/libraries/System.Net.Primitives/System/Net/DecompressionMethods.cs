// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Data contracts from NETStandard.Library.Ref 2.1.0.

namespace System.Net;

[System.Flags]
public enum DecompressionMethods
{
    Deflate = 2,
    GZip = 1,
    None = 0,
}
