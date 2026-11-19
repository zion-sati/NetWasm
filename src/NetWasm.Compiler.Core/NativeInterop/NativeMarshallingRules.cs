// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

// Desktop .NET's two rule sets for the native signatures an assembly declares or calls: its P/Invokes, its
// [UnmanagedCallersOnly] callbacks and its unmanaged function-pointer calls. An assembly with
// [DisableRuntimeMarshalling] passes bool as C's one-byte bool and char as char16_t, as unmanaged structs hold them,
// and its [MarshalAs] customizations have no effect. In any other assembly, bool and char need the runtime
// marshalling NetWasm does not have.
public enum NativeMarshallingRules
{
    Runtime,
    Disabled,
}
