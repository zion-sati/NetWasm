// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record RootDecisionRequest(
    ManagedMethodBody Method,
    int Block,
    CilInstruction Instruction,
    IMembership<EntityKey> AllocatingMethods,
    IMembership<string> AllocatingConstructedMethods)
{
    // Decide as though nothing the instruction throws could reach a handler in the method: whether the point can
    // collect and then go on, rather than only collect while what it throws travels to a handler here.
    public bool IgnoresHandlers { get; init; }
}
