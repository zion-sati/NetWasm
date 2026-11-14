using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseLoopPrefixEmitter(IVectorLoopMethodResolver methods) : IElementwiseLoopPrefixEmitter
{
    private readonly IVectorLoopMethodResolver _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public List<CilInstruction> Emit(
        int test,
        ElementwiseLoop loop,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(localTypes);
        var code = new ElementwiseVectorCode(_methods, loop.Kind, locals, localTypes, loop.Counter);
        var prefix = code.Code;
        var lanes = code.Lanes;
        var testTarget = new CilOperand.BranchTarget(test);
        var stores = loop.Stores;
        var accesses = stores.Select(static store => (store.Array, store.Offset))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => (element.Array, element.Offset)))
            .Concat(loop.Folds.SelectMany(static fold => Elements(fold.Value)).Select(static element => (element.Array, element.Offset)))
            .Concat(loop.Copies.Values.Select(static copy => (copy.Array, copy.Offset)))
            .DistinctBy(static access => (access.Array.Operation, access.Array.Operand, access.Offset))
            .Where(access => !SpanReferences(loop).Any(span => SameArray(span, access.Array)))
            .ToArray();
        var arrays = accesses.Select(static access => access.Array)
            .DistinctBy(static array => (array.Operation, array.Operand))
            .ToArray();

        // i < end, the arrays non-null, every access at i and at end - 1 within
        // its array, and arrays that would depend on one another different.
        code.Counter(0);
        code.Emit(loop.End);
        code.Add(CilOperation.BranchIfGreaterThanOrEqualSigned, testTarget);
        foreach (var array in arrays)
        {
            prefix.Add(Copy(array));
            code.Add(CilOperation.BranchIfFalse, testTarget);
        }
        foreach (var (array, offset) in accesses)
        {
            code.Counter(offset);
            prefix.Add(Copy(array));
            code.Add(CilOperation.LoadArrayLength);
            code.Add(CilOperation.ConvertInt32);
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
            code.Emit(loop.End);
            code.Constant(offset - 1);
            code.Add(CilOperation.Add);
            prefix.Add(Copy(array));
            code.Add(CilOperation.LoadArrayLength);
            code.Add(CilOperation.ConvertInt32);
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
        }
        // Every span index check holds from i to end - 1.
        foreach (var check in loop.Checks)
        {
            code.Counter(check.Offset);
            code.Constant(0);
            code.Add(CilOperation.BranchIfLessThanSigned, testTarget);
            code.Emit(loop.End);
            code.Constant(check.Offset - 1);
            code.Add(CilOperation.Add);
            prefix.Add(Copy(check.Length));
            code.Add(CilOperation.BranchIfGreaterThanOrEqualUnsigned, testTarget);
        }
        foreach (var (first, second) in Hazards(stores)!)
        {
            prefix.Add(Copy(first));
            prefix.Add(Copy(second));
            code.Add(CilOperation.BranchIfEqual, testTarget);
        }

        foreach (var store in stores) code.Hoist(store.Value);
        foreach (var fold in loop.Folds) code.Hoist(fold.Value);
        var folded = code.Identities(loop.Folds);

        // The vector loop, while `lanes` iterations remain.
        var loopStart = prefix.Count;
        code.Emit(loop.End);
        code.Counter(0);
        code.Add(CilOperation.Subtract);
        code.Constant(lanes);
        var exit = prefix.Count;
        code.Add(CilOperation.BranchIfLessThanSigned, testTarget);
        foreach (var store in stores)
        {
            code.Value(store.Value);
            prefix.Add(Copy(store.Array));
            code.Call(_methods.Resolve(VectorLoopMethod.ArrayData, store.Type));
            code.Counter(store.Offset);
            code.Add(CilOperation.ConvertNativeUInt);
            code.Call(_methods.Resolve(VectorLoopMethod.Store, code.Lane));
        }
        code.Fold(folded);
        // An element copy keeps the last iteration's element, as the loop leaves it.
        foreach (var (local, copy) in loop.Copies)
        {
            prefix.Add(Copy(copy.Array));
            code.Counter(copy.Offset + lanes - 1);
            if (copy.Span is { } indexer)
            {
                if (indexer.Conversion is { } conversion) prefix.Add(Copy(conversion));
                prefix.Add(Copy(indexer.Add));
                code.Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(copy.Type));
            }
            else
            {
                code.Add(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(copy.Type));
            }
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        // A counter copy keeps the last iteration's value, as the loop leaves it.
        foreach (var (local, alias) in loop.Aliases)
        {
            code.Counter(lanes - 1);
            if (alias.Scale != 1)
            {
                code.Constant(alias.Scale);
                code.Add(CilOperation.Multiply);
            }
            if (alias.Offset != 0)
            {
                code.Constant(alias.Offset);
                code.Add(CilOperation.Add);
            }
            code.Add(CilOperation.StoreLocal, new CilOperand.Index(local));
        }
        code.Counter(lanes);
        code.Add(CilOperation.StoreLocal, new CilOperand.Index(loop.Counter));
        code.Add(CilOperation.Branch, RenumberedInstructions.Local(loopStart));
        // The vector loop leaves to the folds of each vector's lanes into its
        // local, then to the loop's entry jump, which follows the prefix.
        prefix[exit] = prefix[exit] with { Operand = RenumberedInstructions.Local(prefix.Count) };
        code.Reduce(folded);

        depth = Depth(loop.Stores, loop.Folds);
        return prefix;
    }
}
