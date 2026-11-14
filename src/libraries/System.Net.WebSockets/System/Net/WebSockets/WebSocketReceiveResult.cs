// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;

namespace System.Net.WebSockets
{
    public class WebSocketReceiveResult
    {
        public WebSocketReceiveResult(int count, WebSocketMessageType messageType, bool endOfMessage)
            : this(count, messageType, endOfMessage, null, null)
        {
        }

        public WebSocketReceiveResult(int count,
            WebSocketMessageType messageType,
            bool endOfMessage,
            WebSocketCloseStatus? closeStatus,
            string? closeStatusDescription)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            Count = count;
            EndOfMessage = endOfMessage;
            MessageType = messageType;
            CloseStatus = closeStatus;
            CloseStatusDescription = closeStatusDescription;
        }

        public int Count { get; }
        public bool EndOfMessage { get; }
        public WebSocketMessageType MessageType { get; }
        public WebSocketCloseStatus? CloseStatus { get; }
        public string? CloseStatusDescription { get; }
    }
}
