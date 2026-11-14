using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseWalkMatcher(IElementwiseBodyParser parser) : IElementwiseWalkMatcher
{
    private readonly IElementwiseBodyParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));

    // A walk the vector loop can take: its body only folds and copies the
    // element p points to, of 32-bit or 64-bit integers. It has no counter to
    // index an array with, so every element it reads is one through p, which
    // must read it as the type p walks.
    public ElementwiseWalk? Match(CountedWalk counted, ImmutableArray<CliValueKind> locals)
    {
        ArgumentNullException.ThrowIfNull(counted);
        var (body, pointer, element) = (counted.Body, counted.Reference, counted.Element);
        var written = body
            .Select(static instruction => instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }
                ? local
                : -1)
            .Where(static local => local >= 0)
            .ToHashSet();
        if (_parser.Parse(body, -1, written, pointer) is not ElementwiseBody(var stores, var folds, _, var copies, _) || stores.Count != 0 || folds.Count == 0 ||
            Lane(element) is var kind && kind is not (CliValueKind.I4 or CliValueKind.I8) ||
            folds.SelectMany(static fold => Elements(fold.Value)).Concat(copies.Values)
                .Any(found => found.Type.CanonicalName != element.CanonicalName) ||
            folds.Any(fold => locals[fold.Local] != kind ||
                Kind(fold.Value) is var value && value != kind && value != CliValueKind.Unknown || !Supported(fold.Value, kind)))
        {
            return null;
        }
        return new ElementwiseWalk(pointer, counted.Count, counted.Advance, folds, copies, kind);
    }
}
