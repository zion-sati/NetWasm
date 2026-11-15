// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IExceptionGroupIdAssigner
{
    ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> Assign(
        ImmutableArray<Draft.StructuredExceptionGroupDraft> groups);
}
