using NetWasm.Compiler.Analysis;
namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactSnapshotter
{
    FrontendArtifactSnapshot Capture(FrontendArtifact artifact);
}
