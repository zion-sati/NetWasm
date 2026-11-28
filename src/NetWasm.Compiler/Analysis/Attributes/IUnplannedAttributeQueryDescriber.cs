// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IUnplannedAttributeQueryDescriber
{
    // The NW1019 report of a reference in the holder's code that keeps its query's run-time path.
    UnplannedAttributeQuery Describe(ManagedMethodBody holder, AttributeQueryReference reference,
        AttributeQueryFallback fallback);
}
