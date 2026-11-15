// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

public readonly record struct ManagedCallSiteKey
{
    public ManagedCallSiteKey(ManagedMethodIdentity caller, int instructionOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instructionOffset);
        Caller = caller;
        InstructionOffset = instructionOffset;
    }

    public ManagedMethodIdentity Caller { get; }

    public int InstructionOffset { get; }
}
