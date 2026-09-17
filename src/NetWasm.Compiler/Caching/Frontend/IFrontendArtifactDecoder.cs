using NetWasm.Compiler.Analysis;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactDecoder
{
    FrontendArtifactSnapshot Decode(ImmutableArray<byte> payload);
}
