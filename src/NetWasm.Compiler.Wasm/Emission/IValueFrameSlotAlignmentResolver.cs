// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

// Strategy: the alignment the value-frame planner gives the slot of a value of
// a type whose own storage asks for the given alignment.
internal interface IValueFrameSlotAlignmentResolver
{
    int Resolve(CliTypeIdentity type, int alignment);
}
