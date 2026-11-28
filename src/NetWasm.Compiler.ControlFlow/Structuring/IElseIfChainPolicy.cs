// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Whether an else-if chain of so many conditions, whose arms rejoin at one block, is emitted flat (a labeled block
// each taken arm branches out of, the false path continuing in the same sequence) rather than nested, each later
// condition in the else of the one before. Nesting costs a level of output per arm, which Binaryen and engines walk
// recursively.
internal interface IElseIfChainPolicy
{
    bool Flattens(int conditions);
}

// Flat from a chain of the given number of conditions on.
internal sealed class ElseIfChainPolicy(int minimumConditions) : IElseIfChainPolicy
{
    // A preset's chains, by its structuring size stance: the size-aware presets (Os, Oz) keep chains of two or three
    // conditions nested, which wasm-opt shrinks further (flat, they grew Oz modules by 0.3 to 1.1%); the others flatten
    // from two, which at O3 is both smaller and faster. Long chains flatten at every preset.
    public static ElseIfChainPolicy For(BranchPrefixDuplicationPolicy duplication) =>
        new(duplication == BranchPrefixDuplicationPolicy.SizeAware ? 4 : 2);

    public bool Flattens(int conditions) => conditions >= minimumConditions;
}
