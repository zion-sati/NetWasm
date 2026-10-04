// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading;

public static class Timeout
{
    public static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, Infinite);

    public const int Infinite = -1;
    internal const uint UnsignedInfinite = unchecked((uint)-1);
}
