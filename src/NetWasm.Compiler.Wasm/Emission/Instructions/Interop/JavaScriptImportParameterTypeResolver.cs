// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptImportParameterTypeResolver(
    ITargetLayout layouts) : IJavaScriptImportParameterTypeResolver
{
    // A span passes the address of its value-frame copy, which JavaScript reads
    // through the manifest's span offsets.
    public CliValueKind Resolve(CliTypeIdentity type, bool isCallback) =>
        isCallback || InteropTypeClassifier.IsHostObject(type) || InteropTypeClassifier.IsHandleValue(type)
            ? CliValueKind.I4
            : InteropTypeClassifier.IsDate(type)
                ? CliValueKind.F8
            : JavaScriptMemoryView.IsSupported(type)
                ? CliValueKind.ManagedAddress
                : type.StackKind == CliValueKind.NativeInt
                    ? layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4
                    : type.StackKind;
}
