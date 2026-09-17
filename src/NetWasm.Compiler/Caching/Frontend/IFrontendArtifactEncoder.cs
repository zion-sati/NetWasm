using NetWasm.Compiler.Analysis;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactEncoder
{
    ImmutableArray<byte> Encode(FrontendArtifactSnapshot snapshot);
}
