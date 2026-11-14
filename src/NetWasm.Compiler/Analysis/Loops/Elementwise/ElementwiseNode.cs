using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

// A value an elementwise loop body computes, as the body parser reads it.
internal abstract record ElementwiseNode;

// The counter times Scale plus Offset; a constant has Scale 0.
internal sealed record Linear(int Scale, int Offset) : ElementwiseNode;

// A value the loop does not change, pushed by Code.
internal sealed record Invariant(ImmutableArray<CilInstruction> Code, CliValueKind Kind) : ElementwiseNode;

// Array[counter + Offset], of the element type Type; or, ByReference, the
// element Offset after the one the reference Array points to.
internal sealed record Element(CilInstruction Array, int Offset, CliTypeIdentity Type, bool ByReference = false,
    SpanIndexer? Span = null) : ElementwiseNode;

// How the body indexed a span's reference: Unsafe.Add, after the conversion of
// its index to a native integer, if any.
internal sealed record SpanIndexer(CilInstruction Add, CilInstruction? Conversion);

// Unsafe.Add of a span's reference at the counter plus Offset, which only an
// element load may read.
internal sealed record SpanAddress(CilInstruction Reference, int Offset, CliTypeIdentity Type, SpanIndexer Indexer) : ElementwiseNode;

// The counter plus Offset as a native integer, which only Unsafe.Add may take.
internal sealed record NativeIndex(int Offset, CilInstruction Conversion) : ElementwiseNode;


internal sealed record Unary(CilOperation Operation, ElementwiseNode Operand) : ElementwiseNode;

internal sealed record Binary(CilOperation Operation, ElementwiseNode Left, ElementwiseNode Right) : ElementwiseNode;

// A shift by the constant Count.
internal sealed record Shift(CilOperation Operation, ElementwiseNode Value, int Count) : ElementwiseNode;

// A local's value before this iteration's fold of it.
internal sealed record Accumulator(int Local) : ElementwiseNode;

// The walking reference, which only an element load may read.
internal sealed record Reference(CilInstruction Load) : ElementwiseNode;
