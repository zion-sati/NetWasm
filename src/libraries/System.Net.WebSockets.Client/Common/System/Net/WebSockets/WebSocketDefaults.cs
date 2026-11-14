// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Threading;

namespace System.Net.WebSockets
{
    /// <summary>
    /// Central repository for default values used in WebSocket settings.  Not all settings are relevant
    /// to or configurable by all WebSocket implementations.
    /// </summary>
    internal static partial class WebSocketDefaults
    {
        public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.Zero;
        public static readonly TimeSpan DefaultClientKeepAliveInterval = TimeSpan.FromSeconds(30);

        public static readonly TimeSpan DefaultKeepAliveTimeout = Timeout.InfiniteTimeSpan;
    }
}
