// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiSignaturePlan(
    MethodSignatureModel LogicalSignature,
    MethodSignatureModel PhysicalSignature,
    ImmutableArray<NativeAbiParameterPlan> Parameters,
    NativeAbiValuePlan Result,
    int? HiddenResultParameterIndex);
