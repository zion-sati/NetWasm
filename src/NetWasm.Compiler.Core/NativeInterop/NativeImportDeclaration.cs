// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeImportDeclaration(
    string LibraryName,
    string EntryPoint,
    MethodImportAttributes Attributes,
    bool IsVarArg,
    bool HasMarshalling,
    // Why the C ABI cannot honour the conventions [UnmanagedCallConv] names, or null when it can.
    string? UnsupportedCallingConvention)
{
    // [WasmImportLinkage]: the library name is the Wasm import module, so the
    // host or another module supplies the function rather than a linked archive.
    public bool HasWasmImportLinkage { get; init; }
}
