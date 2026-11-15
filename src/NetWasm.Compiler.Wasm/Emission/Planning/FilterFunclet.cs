// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record FilterFunclet(
    int Id,
    ManagedMethodIdentity CallerIdentity,
    StructuredMethod Method,
    StructuredExceptionClause Clause);

internal readonly record struct ExceptionGroupMetadata(
    int Address,
    int ClauseCount,
    bool HasFilters);
