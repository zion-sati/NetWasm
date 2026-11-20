// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IRuntimeTypeAttributeQueryResolver
{
    // The query a call makes on a target proven only to be a Type (AttributeValueKind.RuntimeType), its Target unset; null
    // when the target is not such a Type or the filter is not one closed attribute type.
    ResolvedAttributeQuery? ResolveRuntimeType(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack);
}
