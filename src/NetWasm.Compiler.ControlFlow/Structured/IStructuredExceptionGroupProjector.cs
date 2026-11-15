// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Draft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredExceptionGroupProjector
{
    StructuredExceptionGroup Project(
        Draft.StructuredExceptionGroupDraft group,
        StructuredExceptionGroupId id,
        Draft.StructuredExceptionGroupDraft? parent,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds);
}
