using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchCandidateIndex
{
    ImmutableArray<DispatchCandidate> Index(DispatchDeclarationCandidate candidate);

    ImmutableArray<DispatchCandidate> Index(DispatchReceiverCandidate candidate);
}
