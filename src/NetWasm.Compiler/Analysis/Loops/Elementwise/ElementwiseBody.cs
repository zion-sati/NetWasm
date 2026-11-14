using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed record Store(CilInstruction Array, int Offset, CliTypeIdentity Type, ElementwiseNode Value);

// Local = Local Operation Value.
internal sealed record Fold(int Local, CilOperation Operation, ElementwiseNode Value);

// The check that the counter plus Offset, unsigned, lies below Length.
internal sealed record Check(int Offset, CilInstruction Length);

// The element stores, folds, counter copies, element copies and span index
// checks of a loop body.
internal sealed record ElementwiseBody(
    List<Store> Stores,
    List<Fold> Folds,
    Dictionary<int, Linear> Aliases,
    Dictionary<int, Element> Copies,
    List<Check> Checks);
