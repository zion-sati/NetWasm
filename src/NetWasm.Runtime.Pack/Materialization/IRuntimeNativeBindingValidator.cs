// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeBindingValidator
{
    ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeBindingValidationRequest request);
    ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeCachedBindingValidationRequest request);
}

internal sealed record RuntimeNativeCachedBindingValidationRequest(
    ImmutableArray<RuntimeNativeBinding> Bindings,
    ImmutableArray<RuntimeValidatedNativeBinding> Evidence,
    RuntimeLinkedModule Module);

internal sealed record RuntimeNativeBindingValidationRequest(
    ImmutableArray<RuntimeNativeBinding> Bindings,
    ImmutableArray<string> PermittedGeneratedInputIdentities,
    ImmutableArray<RuntimeLinkerSymbolEvent> Events,
    RuntimeLinkedModule Module);
