// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryCompileTimeAnswerer
{
    // Answers the call from its argument proofs, its fragment's locals starting at firstLocal.
    AttributeQueryAnswer Answer(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack, int firstLocal);
}
