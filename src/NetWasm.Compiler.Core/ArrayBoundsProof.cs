using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

// Offsets are meaningful only for these exact immutable method snapshots.
public sealed record ArrayBoundsProof(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CliValueKind> Locals,
    ImmutableArray<CliTypeIdentity> LocalSignatureTypes,
    ImmutableHashSet<int> AccessOffsets)
{
    public ImmutableDictionary<int, ArrayIndexOffset> IndexOffsets { get; init; } = [];

    // Array and field accesses whose array or object an earlier access already
    // checked for null.
    public ImmutableHashSet<int> NonNullOffsets { get; init; } = [];

    public bool Describes(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CliValueKind> locals,
        ImmutableArray<CliTypeIdentity> localSignatureTypes) =>
        Instructions == instructions && Locals == locals && LocalSignatureTypes == localSignatureTypes;

    // Whether the access cannot fail its null check.
    public bool ProvesNonNull(int offset) => AccessOffsets.Contains(offset) || NonNullOffsets.Contains(offset);

    // Whether the instruction is one of this snapshot's own.
    public bool Contains(CilInstruction instruction) => InstructionSnapshots.Contains(Instructions, instruction);
}
