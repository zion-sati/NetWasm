// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

// Which dispatched calls take the targets of the receivers the whole program's flow brings them, instead of every
// receiver their declaring type admits.
public enum ReceiverFlowDispatchPolicy
{
    // Generic virtual calls only, whose type-based targets multiply (LINQ's Select over iterators: sources times
    // selects); every other call keeps targets of its own, as the early cutoff, which analyzes each edited method
    // alone, needs.
    GenericVirtualCalls,
    // Every virtual and interface call: each override's code only for the receivers that reach it, a whole-program
    // fact.
    EveryVirtualCall,
}
