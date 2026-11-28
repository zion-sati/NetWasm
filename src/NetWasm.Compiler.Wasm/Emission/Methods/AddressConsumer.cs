// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The instruction that pops the address an instruction pushes, found by walking forward through instructions whose
// stack effects are known; null when one between has no known effect. An ldflda or dup consumes the address it is
// given, so a field reached through `ldflda` names no field of the local itself and is no lane access.
internal static class AddressConsumer
{
    public static int? Find(ImmutableArray<CilInstruction> instructions, int index)
    {
        var above = 0;
        for (var next = index + 1; next < instructions.Length; next++)
        {
            if (CilStackEffects.Of(instructions[next]) is not { } effect) return null;
            if (effect.Pops > above) return next;
            above += effect.Pushes - effect.Pops;
        }
        return null;
    }
}
