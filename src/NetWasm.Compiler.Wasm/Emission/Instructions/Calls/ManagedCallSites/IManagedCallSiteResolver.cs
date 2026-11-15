// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

internal interface IManagedCallSiteResolver
{
    ManagedCallSite Resolve(InstructionEmissionRequest request);
}
