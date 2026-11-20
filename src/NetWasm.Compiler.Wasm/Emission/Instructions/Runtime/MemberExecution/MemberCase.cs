// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

// One member's case of a member execution: its descriptor's address, and the code that runs when the descriptor is
// that one.
internal sealed class MemberCase(int descriptorAddress, Action emit)
{
    public int DescriptorAddress { get; } = descriptorAddress;

    public Action Emit { get; } = emit;
}
