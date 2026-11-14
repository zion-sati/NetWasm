// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Reflection.Emit
{
    public enum PackingSize
    {
        Unspecified = 0,
        Size1 = 1,
        Size2 = 2,
        Size4 = 4,
        Size8 = 8,
        Size16 = 16,
        Size32 = 32,
        Size64 = 64,
        Size128 = 128,
    }
}
