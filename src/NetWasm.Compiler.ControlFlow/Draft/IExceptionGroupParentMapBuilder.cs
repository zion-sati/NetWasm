// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IExceptionGroupParentMapBuilder
{
    IReadOnlyDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?> Build(
        ImmutableArray<StructuredExceptionGroupDraft> exceptionGroups);
}
