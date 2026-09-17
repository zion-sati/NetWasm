using NetWasm.Compiler.Analysis;
namespace NetWasm.Compiler.Caching.Frontend;

internal interface IFrontendArtifactHydrator
{
    FrontendArtifact Hydrate(FrontendArtifactSnapshot snapshot);
}
