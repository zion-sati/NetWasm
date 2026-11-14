using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

// A counted loop the vector loop can take: its counter, the code that pushes its end, and its body.
internal sealed record ElementwiseLoop(
    int Counter,
    CilInstruction[] End,
    List<Store> Stores,
    List<Fold> Folds,
    Dictionary<int, Linear> Aliases,
    Dictionary<int, Element> Copies,
    CliValueKind Kind,
    List<Check> Checks);

// A loop that walks Reference over Count elements: Advance moves the
// reference one element on.
internal sealed record ElementwiseWalk(
    int Reference,
    int Count,
    CilInstruction Advance,
    List<Fold> Folds,
    Dictionary<int, Element> Copies,
    CliValueKind Kind);
