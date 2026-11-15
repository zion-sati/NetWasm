// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilDefectCoverageRequirement(
    string Id,
    string FirstIncorrectLayer,
    string FocusedRegression,
    string? InvalidInvariant,
    ImmutableArray<ImmutableDictionary<string, string>> RequiredNeighborhoods);

internal static class RandomCilDefectCoverageCatalog
{
    public static ImmutableArray<RandomCilDefectCoverageRequirement> Requirements { get; } =
    [
        new(
            "release-structurizer-analysis-reuse",
            "ControlFlowStructurizer graph-analysis cache",
            "ControlFlowStructurizerTests.SequentialDiamondsReuseGraphAnalysesForTheSameBoundary",
            "repeated graph analysis for the same structural boundary",
            [
                Levels(
                    ("cfg", "diamond"),
                    ("order", "reverse-compatible"),
                    ("stack", "empty-join")),
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "finally"),
                    ("order", "interleaved")),
            ]),
        new(
            "rit-illegal-leave-finally",
            "CilExceptionRegionValidator",
            "CilExceptionRegionValidatorTests.ValidatesRegionAwareTransfersAndCleanupTerminators",
            "illegal-leave-from-finally-region",
            [
                Levels(
                    ("cfg", "diamond"),
                    ("eh", "nested-finally"),
                    ("stack", "local-roundtrip")),
            ]),
        new(
            "rit-shared-control-flow-body",
            "ControlFlowStructurizer",
            "ControlFlowStructurizerTests.NonSeseAcyclicOverlapUsesOneWholeRegionDispatcher",
            null,
            [
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "nested-finally"),
                    ("stack", "value-join")),
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "filter-finally"),
                    ("stack", "local-roundtrip")),
            ]),
        new(
            "rit-shared-loop-half-continuation",
            "ControlFlowStructurizer loop partitioning",
            "ControlFlowStructurizerTests.PostTestLoopDoesNotDuplicateAContinuationReachableFromBothLoopHalves",
            "an apparent loop exit must not split a shared continuation into two emitted owners",
            [
                Levels(
                    ("cfg", "overlap"),
                    ("order", "interleaved"),
                    ("stack", "empty-join")),
                Levels(
                    ("cfg", "switch"),
                    ("order", "reverse-compatible"),
                    ("stack", "value-join")),
            ]),
        new(
            "rit-finally-continuation-body",
            "ControlFlowStructurizer",
            "CompilerComplexityGrowthTests.FiveSizesOfEveryGrowthFamilyRemainStructurallyBounded",
            null,
            [
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "nested-finally"),
                    ("stack", "value-join"),
                    ("order", "interleaved")),
            ]),
        new(
            "rit-nested-handled-exception-temporary",
            "ExceptionRegionEmitter",
            "ExceptionRegionEmitterTests.FinallyClearsHandledNestedExceptionBeforeTestingExceptionalExit",
            null,
            [
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "filter-finally"),
                    ("stack", "local-roundtrip"),
                    ("order", "interleaved")),
                Levels(
                    ("cfg", "diamond"),
                    ("eh", "nested-finally"),
                    ("lifetime", "reference-across-allocation"),
                    ("order", "reverse-compatible")),
            ]),
        new(
            "rit-switch-lowering-maxstack",
            "CilSwitchLowerer",
            "CilSwitchLowererTests.LoweredSwitchReservesTwoAdditionalEvaluationStackSlots",
            null,
            [
                Levels(
                    ("cfg", "switch"),
                    ("stack", "value-join"),
                    ("order", "forward")),
                Levels(
                    ("cfg", "switch"),
                    ("eh", "filter-finally"),
                    ("stack", "local-roundtrip"),
                    ("order", "interleaved")),
            ]),
        new(
            "rit-variable-exception-clause-patching",
            "MethodBodyPatcher",
            "MethodBodyPatcherTests.PatchRemovesUnusedReservedExceptionClausesAndShrinksUsedClauses",
            null,
            [
                Levels(
                    ("cfg", "diamond"),
                    ("eh", "none"),
                    ("stack", "empty-join")),
                Levels(
                    ("cfg", "switch"),
                    ("eh", "finally"),
                    ("order", "reverse-compatible")),
                Levels(
                    ("cfg", "overlap"),
                    ("eh", "filter-finally"),
                    ("order", "interleaved")),
        ]),
        new(
            "rit-fixture-reference-local-signature",
            "fixture-template local signature",
            "RandomCilInteractionFixtureFactoryTests.PatchSlotsReserveManagedReferenceLocalForLifetimeInteractions",
            "patch-slot local signatures must match GeneratedCilProgram.Locals",
            [
                Levels(
                    ("lifetime", "reference-across-allocation"),
                    ("data", "argument-local"),
                    ("cfg", "diamond")),
                Levels(
                    ("lifetime", "reference-across-allocation"),
                    ("data", "object-field"),
                    ("call", "direct")),
                Levels(
                    ("lifetime", "reference-across-allocation"),
                    ("stack", "value-join"),
                    ("order", "reverse-compatible")),
        ]),
        new(
            "rit-qualification-runner-observability-and-result-contract",
            "qualification campaign orchestration",
            "RandomCilQualificationFacadeTests.RejectsFailFastPartialResultsAndFailures",
            "standalone RIT execution must expose exact live progress and reject incomplete profile results",
            [
                Levels(
                    ("cfg", "diamond"),
                    ("numeric", "arithmetic"),
                    ("data", "argument-local")),
                Levels(
                    ("cfg", "switch"),
                    ("eh", "finally"),
                    ("call", "direct")),
                Levels(
                    ("cfg", "backedge"),
                    ("lifetime", "reference-across-allocation"),
                    ("order", "interleaved")),
            ]),
        new(
            "rit-template-cache-execution-once",
            "qualification template preparation",
            "ConcurrentPreparationCacheTests.ConcurrentCallersPrepareEachKeyExactlyOnce",
            "concurrent shards must reuse one immutable prepared template per profile and shape",
            [
                Levels(("cfg", "diamond"), ("eh", "finally"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("call", "direct"), ("order", "interleaved")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("stack", "value-join")),
            ]),
        new(
            "rit-cached-template-source-materialization",
            "qualification shard artifact materialization",
            "CorpusSourceArtifactWriterTests.MaterializesTheFixtureSourceInEachShardDirectory",
            "every cached-template shard owns complete source, PE, and PDB evidence",
            [
                Levels(("cfg", "diamond"), ("data", "argument-local"), ("order", "interleaved")),
                Levels(("cfg", "switch"), ("eh", "finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("stack", "value-join"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "rit-progress-single-writer-counters",
            "qualification progress orchestration",
            "RitProgressCounterStoreTests.ProductionReporterWorkersNeverWriteAndHeartbeatOwnsStdout",
            "workers update exact counters while only the orchestration reporter writes stdout",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-node-batch-input-progress",
            "Desktop and Node oracle child-progress orchestration",
            "QualifiedProcessRunnerTests.ConsumesProgressProtocolWithoutPollutingDiagnostics; DesktopOracleRunnerTests.BatchedProtocolMatchesIsolatedPerInputObservations; NetWasmOracleRunnerTests.BatchedNodeProtocolMatchesIsolatedPerInputObservations; OracleOperationProgressReporterTests.ReportsExactExecutionInputProgressWithoutWorkerStdout; RitProgressCounterStoreTests.ProductionReporterWorkersNeverWriteAndHeartbeatOwnsStdout",
            "long-lived oracle children must stream exact input completion into worker counters while only the orchestration reporter writes stdout",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-progress-survives-fail-fast-drain",
            "qualification progress orchestration lifetime",
            "RandomCilCampaignSchedulerTests.HeartbeatContinuesWhileFailedCampaignDrainsActiveCases",
            "the sole stdout heartbeat must remain alive while fail-fast cancellation drains active cases and diagnostic replay runs",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-fail-fast-process-cancellation",
            "campaign cancellation through oracle child-process supervision",
            "QualifiedProcessRunnerTests.CancellationKillsTheProcessTreeAndRemainsCancellation; RandomCilCampaignSchedulerTests.HeartbeatContinuesWhileFailedCampaignDrainsActiveCases",
            "the first campaign failure must cooperatively terminate active external process trees and reach diagnostic replay without completing every remaining oracle stage",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-adaptive-batch-timeout-localization",
            "desktop and Node oracle batch execution",
            "OracleInputBatchingTests.PartitionPreservesEveryInputInBoundedOrder; DesktopOracleRunnerTests.BatchedProtocolUsesReportedProgressToLocalizeTimedOutChunk",
            "a large shard must use bounded chunks and recursively localize a timed-out chunk instead of assigning the whole-batch timeout to its first input",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-exact-mismatch-diagnostic-replay",
            "differential comparison failure identity and campaign replay selection",
            "DifferentialCorpusRunnerTests.RunWritesFailureArtifactBeforeReportingMismatch; RandomCilDiagnosticReplaySelectorTests.SelectCaseMapsExactInputToOwningShardCase",
            "A batched mismatch must preserve its exact input and diagnostic replay must execute only the owning case.",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-shard-profile-worker-bound",
            "standalone qualification workload sizing",
            "RandomCilQualificationWorkloadTests.GetCaseCountIncludesEveryProfileForEveryShard",
            "worker validation must count every profile for every selected shard",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-desktop-oracle-batched-process",
            "desktop oracle process boundary",
            "DesktopOracleRunnerTests.BatchedProtocolMatchesIsolatedPerInputObservations",
            "state-safe RIT shards execute all desktop inputs in one bounded oracle process",
            [
                Levels(("cfg", "diamond"), ("numeric", "conversion"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("cfg", "backedge"), ("call", "direct"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "rit-netwasm-oracle-batched-process",
            "NetWasm oracle process boundary",
            "NetWasmOracleRunnerTests.BatchedNodeProtocolMatchesIsolatedPerInputObservations",
            "state-safe RIT shards execute all target inputs in one bounded Node process",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("cfg", "backedge"), ("call", "direct"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-target-operation-progress-plan",
            "target progress operation planning",
            "OracleOperationProgressReporterTests.ExecutableWasm64UsesTheFullSelectedOperationPlan",
            "progress applicability follows selected validation, execution, and optimization operations",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("call", "direct")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("data", "object-field")),
                Levels(("cfg", "backedge"), ("stack", "value-join"), ("order", "interleaved")),
            ]),
        new(
            "rit-compiler-phase-progress",
            "compiler-host phase progress protocol",
            "OracleOperationProgressReporterTests.ReportsExactCompilerPhaseProgressWithoutDiagnostics",
            "long compiler operations expose exact bounded phase counters without emitting compiler diagnostics",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-end-to-end-program-slicing",
            "qualification generated-program workload sizing",
            "RandomCilInteractionProgramSlicerTests.PartitionsAQualificationShardWithoutLossOrDuplication; RandomCilProgramSliceProgressReporterTests.ReportsExactCompletedAndTotalProgramSlices",
            "each logical shard executes every generated case and input through bounded end-to-end programs without loss or duplication",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-progress-directed-timeout-localization",
            "desktop and Node oracle timeout localization",
            "OracleProgressDirectedBatchingTests.ProgressSplitsAtTheExactCompletedCount; OracleProgressDirectedBatchingTests.ZeroProgressImmediatelyIsolatesTheFirstInput; DesktopOracleRunnerTests.BatchedProtocolUsesReportedProgressToLocalizeTimedOutChunk; DesktopOracleProgressReporterTests.WritesExactBatchAttemptWithoutInputValues; OracleOperationProgressReporterTests.ReportsExactBatchAttemptWithoutInputValues",
            "timeout retries split at exact child progress and expose the active retry size through worker counters without input values",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-profile-paired-desktop-oracle-reuse",
            "qualification campaign orchestration",
            "DifferentialCorpusRunnerTests.RunAgainstOracleDoesNotExecuteDesktopAgain; RandomCilQualificationWorkloadTests.GetCaseCountTreatsEachProfilePairAsOneShardUnit; RandomCilQualificationFacadeTests.AcceptsEveryExpectedSuccessfulPairedShardResult",
            "each semantic shard runs the desktop Debug oracle once, then compares both NetWasm Debug and Release profiles against those immutable observations as one scheduler unit",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "reverse-compatible")),
            ]),
        new(
            "rit-backedge-semantic-local-termination",
            "RandomCilInteractionGenerator",
            "RandomCilInteractionGeneratorTests.BackedgeUsesDedicatedBoundedLoopCounter",
            "backedge loop control must not alias a semantic value mutated by an independently selected family",
            [
                Levels(("cfg", "backedge"), ("numeric", "arithmetic"), ("stack", "local-roundtrip"), ("data", "argument-local")),
                Levels(("cfg", "backedge"), ("numeric", "conversion"), ("order", "reverse-compatible"), ("data", "object-field")),
                Levels(("cfg", "backedge"), ("eh", "filter-finally"), ("lifetime", "reference-across-allocation"), ("order", "interleaved")),
            ]),
        new(
            "complexity-evidence-explicit-diagnostics",
            "complexity qualification evidence plumbing",
            "ComplexityGrowthFixtureFactoryTests.GeneratesFiveDeterministicIncreasingMultiFamilyFixtures; CompilerComplexityMetricsCollectorTests.RecordsComparableStructuralAndEmissionMetricsForEveryManagedMethod; CompilerComplexityGrowthTests.FiveSizesOfEveryGrowthFamilyRemainStructurallyBounded",
            "complexity fixtures explicitly opt into metrics diagnostics while ordinary passing RIT remains trace-free, and reports use stable method display identities rather than opaque metadata tokens",
            [
                Levels(("cfg", "diamond"), ("numeric", "arithmetic"), ("data", "argument-local")),
                Levels(("cfg", "switch"), ("eh", "filter-finally"), ("call", "direct")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("order", "interleaved")),
            ]),
        new(
            "runtime-jit-explicit-variant-interface-dispatch",
            "DispatchTargetResolver.ResolveImplementation",
            "GenericMethodDispatchCompilationTests.ExplicitVariantInterfacesSelectTheExactConstructedDeclaration",
            "multiple explicit implementations sharing one generic interface definition select the exact constructed declaration instead of crashing or choosing by path order",
            [
                Levels(("call", "interface"), ("data", "object-field"), ("order", "interleaved")),
                Levels(("call", "virtual"), ("cfg", "diamond"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "interface"), ("eh", "finally"), ("stack", "value-join")),
            ]),
        new(
            "runtime-jit-readonly-array-address-prefix",
            "CilDecoder and TypedStackValidator",
            "TypedStackValidatorTests.ValidatorAcceptsReadonlyArrayAddressAndRejectsInvalidPlacement; RuntimeJitCorpusTests.EveryWhitelistedCohortPassesSameIlDifferentialExecution",
            "readonly. is accepted only immediately before ldelema and preserves the managed-address semantics",
            [
                Levels(("data", "array"), ("cfg", "diamond"), ("call", "direct")),
                Levels(("data", "array"), ("eh", "finally"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "array"), ("cfg", "backedge"), ("stack", "value-join")),
            ]),
        new(
            "runtime-jit-static-array-rva-initializer",
            "CilDecoder, MethodSpecializer, and ArrayInstructionEmitter",
            "RuntimeJitCorpusTests.CompilerGeneratedStaticArrayInitializerMatchesDesktopDotNet",
            "field-handle initializers retain exact RVA bytes and initialize the allocated array before observation",
            [
                Levels(("data", "static-array-initializer"), ("cfg", "diamond"), ("call", "direct")),
                Levels(("data", "static-array-initializer"), ("eh", "finally"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "static-array-initializer"), ("cfg", "backedge"), ("stack", "value-join")),
            ]),
        new(
            "modern-csharp-static-abstract-interface-call",
            "RuntimeFeature contract, TypedStackValidator, and MethodSpecializer",
            "GenericMethodDispatchCompilationTests.CompilerSpecializesStaticAbstractInterfaceCalls; TypedStackValidatorTests.ValidatorAcceptsConstrainedValueReceiverAndRejectsMalformedOperands",
            "closed static-abstract interface calls use constrained. followed by call and must resolve to the selected concrete static implementation",
            [
                Levels(("call", "static-interface"), ("data", "argument-local"), ("cfg", "diamond")),
                Levels(("call", "static-interface"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("call", "static-interface"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "modern-csharp-default-interface-dispatch",
            "DispatchTargetResolver.ResolveImplementation",
            "ModernLanguageCompilationTests.CompilerLowersDefaultInterfacesAndCovariantReturns",
            "an inherited concrete interface body remains a dispatch target when the receiver class has no override",
            [
                Levels(("call", "default-interface"), ("cfg", "diamond"), ("data", "argument-local")),
                Levels(("call", "default-interface"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("call", "default-interface"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "modern-csharp-covariant-return-dispatch",
            "CoreLib compiler contracts and DispatchTargetResolver",
            "ModernLanguageCompilationTests.CompilerLowersDefaultInterfacesAndCovariantReturns",
            "a virtual override with a more-derived return type occupies the base declaration's dispatch contract",
            [
                Levels(("call", "covariant-return"), ("cfg", "diamond"), ("data", "object-field")),
                Levels(("call", "covariant-return"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("call", "covariant-return"), ("cfg", "overlap"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "accepted-cil-portable-finite-conversion-and-alignment-gap",
            "CilDecoder, TypedStackValidator, and numeric/prefix emitters",
            "CilDecoderTests.DecoderRetainsCoreLibNumericOperationsAndConversionMetadata; RandomCilInteractionGeneratorTests.UnsignedFiniteAndAlignmentPrefixesInteractWithEhAndMemory",
            "conv.r.un, ckfinite, unaligned., and break require explicit decoding, validation, emission, and semantic interaction coverage",
            [
                Levels(("numeric", "unsigned-finite"), ("call", "direct"), ("data", "array")),
                Levels(("numeric", "unsigned-finite"), ("eh", "finally"), ("stack", "value-join")),
                Levels(("numeric", "unsigned-finite"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "rit-case-id-hash-collision",
            "RandomCilInteractionMatrix identity construction",
            "RandomCilInteractionShardPlannerTests.ShardsExhaustiveMatrixWithoutLossOrDuplication",
            "every exhaustive case ID includes its deterministic ordinal so identity uniqueness does not depend on a finite hash",
            [
                Levels(("numeric", "unsigned-finite"), ("call", "direct"), ("cfg", "diamond")),
                Levels(("call", "default-interface"), ("data", "array"), ("eh", "finally")),
                Levels(("call", "covariant-return"), ("cfg", "backedge"), ("order", "interleaved")),
            ]),
        new(
            "qualification-stale-fault-unsupported-boundary",
            "qualification manifest and semantic coverage",
            "FaultExceptionRegionCompilationTests.FaultHandlerRunsOnlyForExceptionalExit; RandomCilInteractionGeneratorTests.FaultCleanupInteractsWithDispatchAndReferenceLifetime",
            "fault cleanup executes only during exceptional transfer and continues propagation into the enclosing handler",
            [
                Levels(("eh", "fault-catch"), ("call", "static-interface"), ("cfg", "overlap")),
                Levels(("eh", "fault-catch"), ("data", "array"), ("stack", "value-join")),
                Levels(("eh", "fault-catch"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "modern-csharp-index-range-and-string-intrinsic-overload",
            "CoreLib Index/Range profile and RuntimeIntrinsicRegistry",
            "ModernLanguageCompilationTests.CompilerLowersIndexAndRangeAcrossArraysSpansAndStrings; RandomCilInteractionGeneratorTests.RangeSliceInteractsWithFaultAndVirtualDispatch",
            "intrinsic recognition is signature-specific and range lowering works across arrays, spans, and strings",
            [
                Levels(("data", "range-slice"), ("eh", "fault-catch"), ("call", "virtual")),
                Levels(("data", "range-slice"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "range-slice"), ("call", "static-interface"), ("stack", "value-join")),
            ]),
        new(
            "rit-multiple-rva-initializer-token-identity",
            "RandomCilMetadataTokenResolver",
            "RandomCilGeneratorTests.SameMetadataSeedAndProfileProduceByteIdenticalPatchedAssemblies",
            "the RIT SeedData initializer is resolved from its owning static-field assignment rather than assuming the assembly has one RVA field",
            [
                Levels(("data", "static-array-initializer"), ("call", "direct"), ("cfg", "diamond")),
                Levels(("data", "range-slice"), ("eh", "fault-catch"), ("order", "interleaved")),
                Levels(("data", "static-array-initializer"), ("call", "static-interface"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "multi-exit-loop-shared-continuation-ownership",
            "ControlFlowStructurizer and dispatcher exit ownership",
            "StatementCompilationTests.CompilerBreakSkipsTheRemainderOfAPostTestLoopBody; DeterministicGeneratedCorpusTests.GeneratedControlFlow1357246806",
            "a multi-exit loop preserves break/continue destinations without cloning or executing the shared continuation twice",
            [
                Levels(("cfg", "backedge"), ("order", "reverse-compatible"), ("stack", "empty-join")),
                Levels(("cfg", "overlap"), ("eh", "finally"), ("order", "interleaved")),
                Levels(("cfg", "backedge"), ("lifetime", "reference-across-allocation"), ("stack", "value-join")),
            ]),
        new(
            "finally-continuation-to-loop-exit-ownership",
            "ControlFlowStructurizer exception-continuation classification",
            "ControlFlowStructurizerTests.FinallyLeaveToTheLoopExitBecomesAStructuredBreak; DeterministicGeneratedCorpusTests.GeneratedExceptionTransfer2468135700",
            "a finally continuation targeting the enclosing loop exit is a structured break and does not own the post-loop continuation",
            [
                Levels(("cfg", "backedge"), ("eh", "finally"), ("order", "interleaved")),
                Levels(("cfg", "overlap"), ("eh", "nested-finally"), ("stack", "value-join")),
                Levels(("cfg", "backedge"), ("eh", "filter-finally"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "exception-continuation-to-loop-continue-ownership",
            "ControlFlowStructurizer loop continuation ownership",
            "ControlFlowStructurizerTests.ExceptionContinuationsShareOneStructuredLoopContinueBody; DeterministicGeneratedCorpusTests.GeneratedExceptionTransfer2468135702",
            "multiple exception continuations converging inside a loop branch to one owned continue body instead of cloning it",
            [
                Levels(("cfg", "backedge"), ("eh", "filter-finally"), ("order", "interleaved")),
                Levels(("cfg", "overlap"), ("eh", "nested-finally"), ("stack", "value-join")),
                Levels(("cfg", "backedge"), ("eh", "finally"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "constrained-wide-enum-hash-identity",
            "MethodSpecializer and EnumIntrinsicEmitter",
            "EqualityComparerCompilationTests.CompilerExecutesDirectAndBoxedEnumHashing",
            "a constrained enum intrinsic retains its closed wide underlying type through specialization",
            [
                Levels(("data", "boxed-value"), ("call", "static-interface"), ("stack", "local-roundtrip")),
                Levels(("data", "boxed-value"), ("call", "virtual"), ("cfg", "diamond")),
                Levels(("data", "boxed-value"), ("lifetime", "reference-across-allocation"), ("order", "interleaved")),
            ]),
        new(
            "exception-return-epilogue-ownership",
            "ControlFlowStructurizer continuation ownership",
            "PrimitiveCompilationTests.CompilerExecutesInvariantIntegerContracts; PrimitiveCompilationTests.CompilerExecutesFloatingPointAndMathContracts",
            "sequential exception exits may copy a shared return epilogue synthetically while exactly one emission owns each original block",
            [
                Levels(("eh", "fault-catch"), ("cfg", "overlap"), ("order", "forward")),
                Levels(("eh", "finally"), ("cfg", "overlap"), ("stack", "value-join")),
                Levels(("eh", "nested-finally"), ("cfg", "diamond"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "final-rit-summary-contract",
            "standalone qualification CLI evidence boundary",
            "RitQualificationSummaryTests.AtomicSummaryRoundTripsOnlyCompleteExhaustivePassEvidence; RitQualificationSummaryTests.IncompleteOrFailedSummaryIsRejected",
            "a standalone exhaustive pass atomically records live-derived program and paired-shard totals for the final audit",
            [
                Levels(("call", "direct"), ("cfg", "diamond"), ("data", "array")),
                Levels(("call", "interface"), ("eh", "finally"), ("order", "interleaved")),
                Levels(("call", "indirect"), ("lifetime", "reference-across-allocation"), ("stack", "local-roundtrip")),
            ]),
        new(
            "terminal-exception-boundary-and-reachable-map",
            "WasmModuleEmitter",
            "ExceptionTerminationCompilationTests.ExceptionObjectContractIsStacklessCatchableAndAutomaticallyMapped; ExceptionTerminationCompilationTests.TerminalUnwindPreservesRethrowFilterAndCleanupReplacementSemantics; ExceptionTerminationCompilationTests.ReachableMapIncludesExceptionsRaisedOnlyByCilSemantics",
            "Outward managed failures must preserve rooted exception state, borrowed messages, and reachable type identity.",
            [Levels(("eh", "finally"), ("call", "direct"), ("lifetime", "reference-across-allocation"))]),
        new(
            "constructed-generic-exception-assembly-identity",
            "ReachableExceptionTypePlanner",
            "ReachableExceptionTypePlannerTests.ConstructedTypeUsesItsGenericDefinitionAssemblyIdentity",
            "Closed constructed exception types must retain canonical declaring-assembly identity in diagnostic maps.",
            [
                Levels(("call", "direct"), ("data", "object-field"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "indirect"), ("data", "boxed-value"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "virtual"), ("data", "array"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "exception-constructor-hresult-contract",
            "NetWasm.CoreLib",
            "ExceptionTerminationCompilationTests.SupportedExceptionConstructorsUseDesktopHResults",
            "Supported base and derived exception constructors must preserve their desktop HRESULT identities.",
            [
                Levels(("eh", "finally"), ("call", "direct"), ("stack", "local-roundtrip")),
                Levels(("eh", "filter-finally"), ("call", "indirect"), ("stack", "value-join")),
                Levels(("eh", "nested-finally"), ("call", "virtual"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "reflection-free-runtime-type-contract",
            "NetWasm.CoreLib runtime type facade",
            "RuntimeTypeCompilationTests.CompilerExecutesDemandedRuntimeTypeNames",
            "Demanded runtime type names use bounded retained metadata without enabling runtime discovery.",
            [
                Levels(("call", "direct"), ("eh", "fault-catch"), ("data", "object-field")),
                Levels(("call", "virtual"), ("eh", "finally"), ("stack", "local-roundtrip")),
                Levels(("call", "indirect"), ("eh", "filter-finally"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "dispatcher-internal-conditional-join",
            "ControlFlowStructurizer",
            "ControlFlowStructurizerTests.ExternalEntryIntoLoopBodyUsesOneDispatcherWithoutDuplicateBlocks",
            "A conditional join inside a multiply entered dispatcher must move to the dispatcher's external join.",
            [
                Levels(("cfg", "overlap"), ("stack", "local-roundtrip"), ("order", "forward")),
                Levels(("cfg", "backedge"), ("stack", "value-join"), ("order", "reverse-compatible")),
                Levels(("cfg", "diamond"), ("lifetime", "reference-across-allocation"), ("order", "interleaved")),
            ]),
        new(
            "exception-continuation-join-outside-region",
            "ControlFlowStructurizer",
            "ControlFlowStructurizerTests.ConditionalContinuationCanSkipTheFollowingExceptionGroup",
            "A conditional exception continuation may target the following protected group outside the current structurization boundary.",
            [
                Levels(("cfg", "diamond"), ("eh", "finally"), ("order", "forward")),
                Levels(("cfg", "overlap"), ("eh", "nested-finally"), ("stack", "value-join")),
                Levels(("cfg", "backedge"), ("eh", "filter-finally"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "non-generic-array-copy-clear-runtime",
            "System.Array runtime ABI and collector reference-store abstraction",
            "ArrayNonGenericCompilationTests.NonGenericCopyAndClearExecuteForEveryProfile",
            "Non-generic array movement must preserve overlap, reference assignability, embedded roots, value layout, target width, and clearing semantics.",
            [
                Levels(("data", "array"), ("cfg", "diamond"), ("call", "direct")),
                Levels(("data", "array"), ("eh", "finally"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "array"), ("cfg", "backedge"), ("stack", "value-join")),
            ]),
        new(
            "array-clone-runtime-representation-family",
            "System.Array clone intrinsic and runtime ABI",
            "ArrayCloneCompatibilityTests.ArrayCloneMatchesDesktopAcrossProfilesAndTargets",
            "Array cloning must allocate a distinct array of the same runtime representation and shallow-copy references, values, embedded references, and rectangular shapes.",
            [
                Levels(("data", "array"), ("call", "direct"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "boxed-value"), ("cfg", "diamond"), ("stack", "value-join")),
                Levels(("data", "object-field"), ("eh", "finally"), ("call", "virtual")),
            ]),
        new(
            "canonical-boxed-value-layout-family",
            "native runtime value-frame ABI",
            "complex-mutable-collections native acceptance Debug and Release across wasm32 and wasm64",
            "The value-frame allocation-size parameter must use the target native width even while the current implementation enforces a 32-bit frame-capacity limit.",
            [
                Levels(("data", "boxed-value"), ("call", "direct"), ("stack", "local-roundtrip")),
                Levels(("data", "array"), ("cfg", "diamond"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "indirect"), ("eh", "finally"), ("stack", "value-join")),
            ]),
        new(
            "wasm64-value-frame-enter-native-width",
            "native runtime value-frame ABI",
            "complex-mutable-collections native acceptance Debug and Release across wasm32 and wasm64",
            "The value-frame allocation-size parameter must use the target native width even while the current implementation enforces a 32-bit frame-capacity limit.",
            [
                Levels(("data", "boxed-value"), ("call", "direct"), ("stack", "local-roundtrip")),
                Levels(("data", "array"), ("cfg", "diamond"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "indirect"), ("eh", "finally"), ("stack", "value-join")),
            ]),
        new(
            "constrained-object-type-intrinsic-receiver",
            "IntrinsicCallRewriter and TypeMaterializationEmitter",
            "MethodSpecializerTests.IntrinsicRulePreservesConstrainedObjectTypeReceiverContract; TypeMaterializationEmitterTests.ConstrainedObjectTypeMaterializesFromValueOrReferenceReceivers",
            "Intrinsic lowering must preserve constrained managed-address receivers and materialize the exact runtime type for value and reference instantiations.",
            [
                Levels(("call", "virtual"), ("data", "boxed-value"), ("stack", "local-roundtrip")),
                Levels(("call", "interface"), ("cfg", "diamond"), ("lifetime", "reference-across-allocation")),
                Levels(("call", "direct"), ("eh", "finally"), ("data", "object-field")),
            ]),
        new(
            "szarray-generic-interface-dispatch-family",
            "TypeRelationshipClassifier, DispatchTargetResolver, and SZArrayHelper",
            "DispatchActorTests.DispatchTargetResolverMapsArrayInterfacesToRuntimeHelpers; ImmutableArrayCompatibilityCorpusTests.ImmutableArraysMatchDesktopForEveryCompilerProfileAndTarget",
            "Single-dimensional arrays implement the complete applicable generic mutable and read-only collection interface family rather than enumeration alone.",
            [
                Levels(("call", "interface"), ("data", "array"), ("cfg", "diamond")),
                Levels(("call", "interface"), ("data", "array"), ("eh", "finally")),
                Levels(("call", "interface"), ("data", "array"), ("lifetime", "reference-across-allocation")),
            ]),
        new(
            "constructed-generic-base-object-layout",
            "ManagedObjectLayoutBuilder",
            "ConstructedGenericCastCompilationTests.CompilerLaysOutNonGenericClassesThroughConstructedGenericBaseChains",
            "Object layout must preserve closed type arguments through every constructed generic base in a non-generic derived hierarchy.",
            [
                Levels(("data", "object-field"), ("call", "direct"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "object-field"), ("call", "virtual"), ("cfg", "diamond")),
                Levels(("data", "boxed-value"), ("call", "interface"), ("stack", "local-roundtrip")),
            ]),
        new(
            "hashset-null-element-contract",
            "NetWasm.CoreLib HashSet<T>",
            "HashCollectionCompilationTests.HashSetPreservesNullElementContracts",
            "HashSet reference and nullable element specializations must preserve one null slot independently of Dictionary's non-null key contract.",
            [
                Levels(("data", "object-field"), ("cfg", "diamond"), ("call", "interface")),
                Levels(("data", "array"), ("cfg", "backedge"), ("lifetime", "reference-across-allocation")),
                Levels(("data", "object-field"), ("eh", "finally"), ("stack", "local-roundtrip")),
            ]),
    ];

    private static ImmutableDictionary<string, string> Levels(
        params (string Dimension, string Level)[] values) =>
        values.ToImmutableDictionary(
            item => item.Dimension,
            item => item.Level,
            StringComparer.Ordinal);
}
