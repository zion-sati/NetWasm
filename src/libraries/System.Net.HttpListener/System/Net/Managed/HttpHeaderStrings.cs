// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net
{
    internal static class HttpHeaderStrings
    {
        internal const string Charset = "charset=";
        internal const string NetCoreServerName = "Microsoft-NetCore/2.0";
        internal const string Close = "close";
        internal const string Chunked = "chunked";
        internal const string KeepAlive = "keep-alive";
    }
}
