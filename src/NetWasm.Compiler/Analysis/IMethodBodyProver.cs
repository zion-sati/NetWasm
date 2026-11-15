// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Plans the array, null and integer check proofs of a body, for the instructions it has.</summary>
internal interface IMethodBodyProver
{
    CilMethodBody Prove(CilMethodBody body);
}
