// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportResolveTypeResolver(ITargetLayout layouts) :
    IAsyncJSImportResolveTypeResolver
{
    public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding)
    {
        var result = binding.Return.ResultType;
        if (result is null)
        {
            return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4);
        }

        if (InteropTypeClassifier.IsJavaScriptReference(result))
        {
            return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, CliValueKind.I4);
        }
        if (InteropTypeClassifier.IsDate(result))
        {
            return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, CliValueKind.F8);
        }
        if (JavaScriptNullable.Underlying(result) is { } underlying)
        {
            // The settled value's presence, then the value as a parameter takes it.
            return WasmFunctionType.Create(
                CliValueKind.Void, CliValueKind.I4, CliValueKind.I4, ScalarKind(underlying));
        }

        return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, ScalarKind(result));
    }

    private CliValueKind ScalarKind(CliTypeIdentity type) => InteropTypeClassifier.IsDate(type)
        ? CliValueKind.F8
        : type.StackKind == CliValueKind.NativeInt
            ? layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4
            : type.StackKind;
}
