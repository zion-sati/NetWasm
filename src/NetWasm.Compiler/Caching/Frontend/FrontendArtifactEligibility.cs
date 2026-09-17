using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed record FrontendArtifactEligibilityRequest(
    AssemblyIdentity EntryAssembly,
    ReachableMethodAnalysis Analysis,
    StructuredMethod StructuredMethod);

internal enum FrontendArtifactEligibility
{
    Eligible,
    ReferencesEntryAssembly,
}

internal interface IFrontendArtifactEligibilityClassifier
{
    FrontendArtifactEligibility Classify(FrontendArtifactEligibilityRequest request);
}
