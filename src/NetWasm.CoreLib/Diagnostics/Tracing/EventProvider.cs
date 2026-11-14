// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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
