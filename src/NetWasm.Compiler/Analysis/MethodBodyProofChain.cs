using System;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The proof passes that close the managed optimizer, for a body a whole-program reduction rewrote after them: each
// plans from the instructions it is given and rewrites none, so the proofs describe the body that is emitted. The
// bounds proof comes first, as the others add to it.
internal sealed class MethodBodyProofChain(
    IArrayBoundsCheckEliminator arrayBounds,
    IRepeatedArrayAccessEliminator repeatedAccesses,
    IConstructedObjectNullCheckEliminator constructedObjects,
    IIntegerCheckEliminator integerChecks) : IMethodBodyProver
{
    private readonly IArrayBoundsCheckEliminator _arrayBounds = arrayBounds ?? throw new ArgumentNullException(nameof(arrayBounds));
    private readonly IRepeatedArrayAccessEliminator _repeatedAccesses = repeatedAccesses ??
        throw new ArgumentNullException(nameof(repeatedAccesses));
    private readonly IConstructedObjectNullCheckEliminator _constructedObjects = constructedObjects ??
        throw new ArgumentNullException(nameof(constructedObjects));
    private readonly IIntegerCheckEliminator _integerChecks = integerChecks ?? throw new ArgumentNullException(nameof(integerChecks));

    public CilMethodBody Prove(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return _integerChecks.Rewrite(_constructedObjects.Rewrite(_repeatedAccesses.Rewrite(_arrayBounds.Rewrite(body))));
    }
}
