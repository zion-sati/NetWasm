using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseLaneSelector : IElementwiseLaneSelector
{
    public CliValueKind? Select(ElementwiseBody body, ImmutableArray<CliValueKind> locals)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (stores, folds, copies) = (body.Stores, body.Folds, body.Copies);
        // One lane type for every element the loop touches; folds of integers
        // into locals of that kind, of arrays the loop does not write.
        var kinds = stores.Select(static store => Lane(store.Type))
            .Concat(stores.SelectMany(static store => Elements(store.Value)).Select(static element => Lane(element.Type)))
            .Concat(folds.SelectMany(static fold => Elements(fold.Value)).Select(static element => Lane(element.Type)))
            .Distinct()
            .ToArray();
        // A loop that only folds 64-bit lanes does two elements a vector
        // iteration, which the array unroller's four scalar ones outrun
        // (StrengthReduction's SumLongsArray: 3.25 us unrolled, 4.54 as vectors).
        if (kinds is not [{ } kind] || kind == CliValueKind.Unknown ||
            stores.Count == 0 && kind is CliValueKind.I8 ||
            stores.Any(store => Kind(store.Value) != kind || !Supported(store.Value, kind)) ||
            folds.Any(fold => kind is not (CliValueKind.I4 or CliValueKind.I8) || locals[fold.Local] != kind ||
                Kind(fold.Value) is var value && value != kind && value != CliValueKind.Unknown ||
                !Supported(fold.Value, kind) ||
                Elements(fold.Value).Any(element => stores.Any(store => SameArray(store.Array, element.Array)))))
        {
            return null;
        }
        var spans = stores.SelectMany(static store => Elements(store.Value))
            .Concat(folds.SelectMany(static fold => Elements(fold.Value)))
            .Concat(copies.Values)
            .Any(static element => element.Span is not null);
        return Hazards(stores) is null || spans && stores.Count != 0 ||
            copies.Values.Any(copy => Lane(copy.Type) != kind || stores.Any(store => SameArray(store.Array, copy.Array)))
            ? null
            : kind;
    }
}
