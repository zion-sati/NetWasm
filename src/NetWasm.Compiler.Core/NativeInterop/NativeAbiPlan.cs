// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiPlan(
    NativeImportDeclaration Import,
    NativeAbiSignaturePlan Lowering)
{
    public MethodSignatureModel Signature => Lowering.PhysicalSignature;
}
