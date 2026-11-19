// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Proves the assembly a call returns, when the compile knows it.
internal interface IAttributeAssemblySources
{
    AttributeValueProof? Prove(CilOperation operation, MethodInstanceModel method, List<AttributeValueProof> stack);
}
