// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A lowered method's frame type, RuntimeAsyncFrame over its slots, parameters and result, and the fields that lead from
// a frame to each slot.
internal sealed record RuntimeAsyncFrameLayout(CliTypeIdentity Type, ImmutableArray<ImmutableArray<FieldInstanceModel>> Paths);

internal interface IRuntimeAsyncFrameLayoutResolver
{
    // The frame for slots of these types, in order, and these parameters and result.
    RuntimeAsyncFrameLayout Resolve(ImmutableArray<CliTypeIdentity> slots, ImmutableArray<CliTypeIdentity> parameters,
        CliTypeIdentity returnType);
}
