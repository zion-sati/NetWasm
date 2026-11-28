// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Which instructions write a static field: a store, or its address taken for anything but an immediate pop (which
// only runs the owner's static constructor). The unwritten-field fold folds the loads of fields no reachable
// instruction writes, and a method's contribution records the fields it writes, so an edit that adds or removes a
// write is not served from the last compile.
internal static class StaticFieldWrites
{
    // Valid IL never ends a method on an address, so one always follows.
    internal static bool Writes(ImmutableArray<CilInstruction> instructions, int index) =>
        instructions[index].Operation == CilOperation.StoreStaticField ||
        instructions[index].Operation == CilOperation.LoadStaticFieldAddress &&
        instructions[index + 1].Operation != CilOperation.Pop;
}
