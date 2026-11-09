using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.NullChecks;

internal static class NonNullProof
{
    // The body with the offsets added to its proof of non-null references. A proof
    // that describes an earlier snapshot of the body no longer holds, so a new one
    // replaces it.
    public static CilMethodBody Add(CilMethodBody body, ImmutableHashSet<int> offsets)
    {
        var proof = body.ArrayBoundsProof is { } existing &&
            existing.Describes(body.Instructions, body.Locals, body.LocalSignatureTypes)
            ? existing
            : new ArrayBoundsProof(body.Instructions, body.Locals, body.LocalSignatureTypes, []);
        return body with { ArrayBoundsProof = proof with { NonNullOffsets = proof.NonNullOffsets.Union(offsets) } };
    }
}
