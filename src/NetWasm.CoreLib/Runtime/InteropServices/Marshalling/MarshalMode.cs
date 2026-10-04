// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

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
