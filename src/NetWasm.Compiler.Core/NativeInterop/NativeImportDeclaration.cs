// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeImportDeclaration(
    string LibraryName,
    string EntryPoint,
    MethodImportAttributes Attributes,
    bool IsVarArg,
    // A [MarshalAs] NetWasm cannot express: one that is more than a single native type, or a native type a marshalling
    // stub neither converts to nor accepts as a restatement (NativeMarshalAsTypes.IsExpressible).
    bool HasMarshalling,
    // Why the C ABI cannot honor the conventions [UnmanagedCallConv] names, or null when it can.
    string? UnsupportedCallingConvention)
{
    // [WasmImportLinkage]: the library name is the Wasm import module, so the
    // host or another module supplies the function rather than a linked archive.
    public bool HasWasmImportLinkage { get; init; }

    // PreserveSig is clear: desktop .NET turns the native result into an HRESULT it throws on, and passes the
    // managed result back through a trailing pointer. That is runtime marshalling; a call without it would
    // return the raw native value.
    public bool TranslatesHResult { get; init; }

    // Each parameter's [In] and [Out] attributes.
    public NativeParameterFlows ParameterFlows { get; init; } = NativeParameterFlows.None;

    // The result's and each parameter's [MarshalAs] native type, where one is given.
    public NativeMarshalAsTypes MarshalAs { get; init; } = NativeMarshalAsTypes.None;
}
