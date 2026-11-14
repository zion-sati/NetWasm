// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net.Sockets
{
    public struct SocketReceiveFromResult
    {
        public int ReceivedBytes;
        public EndPoint RemoteEndPoint;
    }

    // Alternative:
    //    public struct SocketReceiveFromResult
    //    {
    //        public int ReceivedBytes;
    //        public IPAddress Address;
    //        public int Port;
    //    }
}
