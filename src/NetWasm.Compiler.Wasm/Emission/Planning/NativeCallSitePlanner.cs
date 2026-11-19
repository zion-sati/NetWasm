// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class NativeCallSitePlanner(INativeAbiSignaturePlanner signatures) : INativeCallSitePlanner
{
    private const string TrampolinePrefix = "netwasm_calli_";

    private readonly INativeAbiSignaturePlanner _signatures =
        signatures ?? throw new ArgumentNullException(nameof(signatures));

    public NativeCallSitePlan Plan(IEnumerable<NativeCallSiteUse> uses, int firstImportIndex)
    {
        ArgumentNullException.ThrowIfNull(uses);
        ArgumentOutOfRangeException.ThrowIfNegative(firstImportIndex);
        // The first use of each signature under each assembly's rules, in a stable order, names it in a
        // diagnostic. A signature both rule sets accept lowers the same under either.
        var lowerings = uses
            .OrderBy(use => use.Method, StringComparer.Ordinal)
            .ThenBy(use => use.Offset)
            .DistinctBy(use => (NativeCallSitePlan.Key(use.Signature), use.Rules))
            .Select(use => (Key: NativeCallSitePlan.Key(use.Signature), Lowering: Lower(use)))
            .DistinctBy(item => item.Key)
            .ToArray();
        if (lowerings.Length == 0)
            return NativeCallSitePlan.Empty;

        var trampolines = lowerings
            .Select(item => TrampolineType(item.Lowering.PhysicalSignature))
            .DistinctBy(Symbol)
            .OrderBy(Symbol, StringComparer.Ordinal)
            .Select((type, index) => new NativeCallTrampoline(
                Symbol(type), type, new WasmFunctionIndex(firstImportIndex + index)))
            .ToImmutableArray();
        var bySymbol = trampolines.ToDictionary(trampoline => trampoline.Symbol, StringComparer.Ordinal);
        return new(trampolines, lowerings.ToImmutableDictionary(
            item => item.Key,
            item => new NativeCallSiteLowering(
                item.Lowering,
                bySymbol[Symbol(TrampolineType(item.Lowering.PhysicalSignature))]),
            StringComparer.Ordinal));
    }

    private NativeAbiSignaturePlan Lower(NativeCallSiteUse use)
    {
        try
        {
            return _signatures.Plan(use.Signature, NativeAbiSignatureKind.Import, use.Method, use.Rules);
        }
        catch (CompilerException exception) when (exception.Diagnostic.IlOffset is null)
        {
            throw new CompilerException(exception.Diagnostic with { IlOffset = use.Offset });
        }
    }

    // The C ABI arguments, then the function pointer.
    private static WasmFunctionType TrampolineType(MethodSignatureModel physical) =>
        new([.. physical.ParameterTypes, CliValueKind.NativeInt], physical.ReturnType);

    // One name per physical signature: netwasm_calli_ the result, then each parameter.
    private static string Symbol(WasmFunctionType type)
    {
        var name = new StringBuilder(TrampolinePrefix).Append(Code(type.Result));
        foreach (var parameter in type.Parameters.AsSpan(0, type.Parameters.Length - 1))
            name.Append(Code(parameter));
        return name.ToString();
    }

    private static char Code(CliValueKind kind) => kind switch
    {
        CliValueKind.Void => 'v',
        CliValueKind.I4 => 'i',
        CliValueKind.I8 => 'l',
        CliValueKind.F4 => 'f',
        CliValueKind.F8 => 'd',
        CliValueKind.NativeInt => 'p',
        _ => throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
            $"A native call has no C ABI value for {kind}.")),
    };
}
