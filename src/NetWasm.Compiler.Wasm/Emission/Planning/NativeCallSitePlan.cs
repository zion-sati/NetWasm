// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// One unmanaged calli in the program: its call-site signature, where it is, for diagnostics, and the
// marshalling rules of the assembly that makes it.
internal sealed record NativeCallSiteUse(
    MethodSignatureModel Signature,
    string Method,
    int Offset,
    NativeMarshallingRules Rules = NativeMarshallingRules.Runtime);

// The runtime function an unmanaged calli calls (issue 226): it takes the call's C ABI arguments
// and then the function pointer, and calls the pointer through the native function table. The
// native callback object defines one per physical signature.
internal sealed record NativeCallTrampoline(string Symbol, WasmFunctionType Type, WasmFunctionIndex Index);

// An unmanaged call-site signature's lowering, as a DllImport's, and the trampoline it calls.
internal sealed record NativeCallSiteLowering(NativeAbiSignaturePlan Lowering, NativeCallTrampoline Trampoline);

internal sealed record NativeCallSitePlan(
    ImmutableArray<NativeCallTrampoline> Trampolines,
    ImmutableDictionary<string, NativeCallSiteLowering> BySignature)
{
    public static NativeCallSitePlan Empty { get; } = new([], ImmutableDictionary<string, NativeCallSiteLowering>.Empty);

    // Signatures key by their types' canonical names: records compare arrays by reference.
    public static string Key(MethodSignatureModel signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return string.Join("|", signature.ParameterSignatureTypes
            .Select(type => type.CanonicalName)
            .Prepend(signature.ReturnSignatureType.CanonicalName));
    }

    public NativeCallSiteLowering For(MethodSignatureModel signature) =>
        BySignature.TryGetValue(Key(signature), out var lowering)
            ? lowering
            : throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                "An unmanaged calli has no planned trampoline."));
}
