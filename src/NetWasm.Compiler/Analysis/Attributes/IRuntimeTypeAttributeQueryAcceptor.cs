// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IRuntimeTypeAttributeQueryAcceptor
{
    // Decides a query call at its validated stack against the program's run-time types.
    RuntimeTypeAttributeQueryDecision Decide(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack,
        IEnumerable<CliTypeIdentity> runtimeTypes);
}
