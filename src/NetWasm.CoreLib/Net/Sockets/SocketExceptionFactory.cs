// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: the message is Interop.Sys.StrError's (no Marshal.GetPInvokeErrorMessage).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System.Net.Sockets
{
    internal static partial class SocketExceptionFactory
    {
        private static string CreateMessage(int nativeSocketError, EndPoint endPoint)
        {
            return Interop.Sys.StrError(nativeSocketError) + " " + endPoint.ToString();
        }
    }
}
