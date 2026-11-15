// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed record ManagedTypeLayouts(
    ImmutableDictionary<EntityKey, ObjectLayout> Objects,
    ImmutableDictionary<CliTypeIdentity, ObjectLayout> ObjectIdentities,
    ImmutableDictionary<string, ObjectLayout> ObjectsByName,
    ImmutableDictionary<CliTypeIdentity, ObjectLayout> ConstructedObjects,
    ManagedValueLayoutState ValueLayoutState,
    WasmTargetLayout Target,
    int DelegateTargetOffset,
    int DelegateMethodIdOffset,
    int DelegateInvocationListOffset,
    int DelegateInvocationCountOffset);
