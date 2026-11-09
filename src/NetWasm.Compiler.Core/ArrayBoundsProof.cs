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
    // checked for null, and field accesses and callvirts whose object newobj
    // produced or that is the proven-non-null this of a constructor.
    public ImmutableHashSet<int> NonNullOffsets { get; init; } = [];

    // Dimensions of rectangular accesses whose index an earlier access of the
    // same array already checked against that dimension.
    public ImmutableHashSet<(int Offset, int Dimension)> CheckedDimensions { get; init; } = [];

    public bool Describes(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CliValueKind> locals,
        ImmutableArray<CliTypeIdentity> localSignatureTypes) =>
        Instructions == instructions && Locals == locals && LocalSignatureTypes == localSignatureTypes;

    // Whether the access cannot fail its null check.
    public bool ProvesNonNull(int offset) => AccessOffsets.Contains(offset) || NonNullOffsets.Contains(offset);

    // Whether the rectangular access cannot fail its check of the dimension.
    public bool ProvesDimension(int offset, int dimension) => CheckedDimensions.Contains((offset, dimension));

    // Whether the instruction is one of this snapshot's own.
    public bool Contains(CilInstruction instruction) => InstructionSnapshots.Contains(Instructions, instruction);
}
