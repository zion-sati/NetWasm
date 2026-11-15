// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptResultDescriptorSizeResolver(
    ITargetLayout layouts) : IJavaScriptResultDescriptorSizeResolver
{
    public int Resolve(CliTypeIdentity result) => result.StackKind switch
    {
        CliValueKind.I8 or CliValueKind.F8 => sizeof(long),
        _ when InteropTypeClassifier.IsDate(result) => sizeof(double),
        _ when InteropTypeClassifier.IsNullable(result) => JavaScriptNullable.RecordSize,
        CliValueKind.NativeInt when layouts.Target.UsesMemory64 => sizeof(long),
        _ => sizeof(int),
    };
}
