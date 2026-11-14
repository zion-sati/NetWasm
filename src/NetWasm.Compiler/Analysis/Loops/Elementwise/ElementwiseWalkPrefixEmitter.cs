using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseWalkPrefixEmitter(IVectorLoopMethodResolver methods) : IElementwiseWalkPrefixEmitter
{
    private readonly IVectorLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    // The instruction that falls into a walk's head, then the vector loop ahead
    // of the head; `exit` follows the walk.
    public List<CilInstruction> Emit(
        CilInstruction entry,
        ElementwiseWalk walk,
        int exit,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(walk);
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(localTypes);
        var code = new ElementwiseVectorCode(_methods, walk.Kind, locals, localTypes, counter: null);
        var prefix = code.Code;
        prefix.Add(entry);
        var lanes = code.Lanes;
        void Count() => code.Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Count));
        void Advance(int elements)
        {
            code.Add(CilOperation.LoadLocal, new CilOperand.Index(walk.Reference));
            code.Constant(elements);
            prefix.Add(Copy(walk.Advance));
        }

        // Fewer than `lanes` elements, zero included, run as before.
        Count();
        code.Constant(lanes);
        var skip = prefix.Count;
        code.Add(CilOperation.BranchIfLessThanUnsigned);
        foreach (var fold in walk.Folds) code.Hoist(fold.Value);
        var folded = code.Identities(walk.Folds);

        var loopStart = prefix.Count;
        code.Fold(folded);
        // An element copy keeps the last element, as the loop leaves it.
        foreach (var (local, copy) in walk.Copies)
        {
            Advance(lanes - 1);
            code.Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(copy.Type));
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        Advance(lanes);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Reference));
        Count();
        code.Constant(lanes);
        code.Add(CilOperation.Subtract);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(walk.Count));
        Count();
        code.Constant(lanes);
        code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, RenumberedInstructions.Local(loopStart));
        code.Reduce(folded);
        // Nothing left: the loop is done.
        Count();
        code.Add(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(exit));
        prefix[skip] = prefix[skip] with { Operand = RenumberedInstructions.Local(prefix.Count) };

        depth = Depth([], walk.Folds);
        return prefix;
    }
}
