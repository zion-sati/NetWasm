// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Runtime.InteropServices
{
    partial class Marshal
    {
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Marshalling code for the object might not be available")]
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        public unsafe static void PtrToStructure(System.IntPtr ptr, object structure) { throw new global::System.PlatformNotSupportedException("Marshal.PtrToStructure structure marshalling into an existing object is not available on this NetWasm platform."); }
        public unsafe static void PtrToStructure<T>(System.IntPtr ptr, [System.Diagnostics.CodeAnalysis.DisallowNullAttribute] T structure) { throw new global::System.PlatformNotSupportedException("Marshal.PtrToStructure structure marshalling into an existing object is not available on this NetWasm platform."); }
    }
}
