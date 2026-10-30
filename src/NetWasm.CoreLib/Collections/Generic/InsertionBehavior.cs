// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

namespace System.Collections.Generic
{
    internal enum InsertionBehavior : byte
    {
        None = 0,
        OverwriteExisting = 1,
        ThrowOnExisting = 2,
    }
}
