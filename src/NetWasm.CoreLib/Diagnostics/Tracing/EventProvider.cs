// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: only EventProviderType is kept; the disabled EventSource profile registers
// no ETW or EventPipe provider.

namespace System.Diagnostics.Tracing
{
    internal enum EventProviderType
    {
        None = 0,
        ETW,
        EventPipe
    }
}
