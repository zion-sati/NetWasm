// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateInvocationTargetSelector
{
    ImmutableArray<ManagedDelegateBinding> Select(
        MethodInstanceModel invoke,
        ImmutableArray<ManagedDelegateBinding> bindings);
}
