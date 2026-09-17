using NetWasm.Compiler.Analysis;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed record ReachableMethodAnalysisSnapshot(
    MethodInstanceModel Method,
    CilMethodBody Body,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> EntryStacks,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks,
    ImmutableArray<EntityKey> CatchTypes,
    ImmutableArray<ReachabilityExceptionRequirement> Exceptions,
    ReachabilityInstructionAnalysis Instructions);

internal sealed record FrontendArtifactSnapshot(
    ReachableMethodAnalysisSnapshot Analysis,
    StructuredMethodConstruction StructuredMethod);
