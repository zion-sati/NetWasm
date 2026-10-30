// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.

namespace System.Runtime.InteropServices.Marshalling
{
    public enum MarshalMode
    {
        Default,
        ManagedToUnmanagedIn,
        ManagedToUnmanagedRef,
        ManagedToUnmanagedOut,
        UnmanagedToManagedIn,
        UnmanagedToManagedRef,
        UnmanagedToManagedOut,
        ElementIn,
        ElementRef,
        ElementOut,
    }
}
