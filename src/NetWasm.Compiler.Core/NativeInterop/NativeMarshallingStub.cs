// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

// A P/Invoke whose signature takes runtime marshalling NetWasm provides, SafeHandle, CriticalHandle and HandleRef
// arguments and results and arrays of blittable elements: the method compiles to a stub that marshals its arguments
// and calls NativeMethod, the import with the native signature, as desktop .NET's IL stub does.
public sealed record NativeMarshallingStub(EntityKey NativeMethod)
{
    // A call into a C variadic function through one of its fixed-signature declarations: the native method takes the
    // fixed parameters and the address of the buffer the stub stores the rest in.
    public NativeVariadicCall? Variadic { get; init; }

    // A call through an unmanaged function pointer whose signature takes runtime marshalling, as desktop .NET's IL stub
    // marshals an unmanaged calli: the stub takes the call's arguments and then the pointer, and calls the pointer with
    // the native signature. NativeMethod is then the stub itself.
    public NativeFunctionPointerCall? FunctionPointer { get; init; }
}

// The native signature an unmanaged calli's stub calls its pointer with, and the declaration that stands for the
// pointer's: no DllImport attributes, so ANSI chars and strings, BOOL bools, and no [In], [Out] or [MarshalAs].
public sealed record NativeFunctionPointerCall(MethodSignatureModel Native, NativeImportDeclaration Declaration);
