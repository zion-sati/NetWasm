#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-fast}"
artifact_dir="${2:-}"
if [[ "$mode" != fast && "$mode" != extended ]]; then
  echo 'usage: run-compiler-harness-contracts.sh fast|extended [absolute-artifact-directory]' >&2
  exit 2
fi
if [[ -z "$artifact_dir" ]]; then
  artifact_dir="$(mktemp -d "${TMPDIR:-/tmp}/netwasm-harness-contracts.XXXXXX")"
elif [[ "$artifact_dir" != /* ]]; then
  echo 'artifact directory must be absolute' >&2
  exit 2
elif [[ -e "$artifact_dir" ]]; then
  echo 'refusing to reuse an existing artifact directory' >&2
  exit 2
else
  mkdir "$artifact_dir"
fi
cd "$repo_root"

gate_status=0
run_gate() {
  local result
  if "$@"; then
    return 0
  else
    result=$?
    if [[ "$gate_status" == 0 ]]; then
      gate_status="$result"
    fi
  fi
  # Independent gates still run after a RED semantic result. Keep its exit code.
  return 0
}

# These are explicit bounded slices, not aliases for the full compiler suite.
# Fast checks support contracts; extended checks changed fixture composition.
if [[ "$mode" == fast ]]; then
  filter='FullyQualifiedName~CorpusExpectationVerifierTests|FullyQualifiedName~CorpusExecutionVerifierTests|FullyQualifiedName~OracleRuntimeCapabilityVerifierTests|FullyQualifiedName~DifferentialCorpusRunnerTests|FullyQualifiedName~OracleComparerTests|FullyQualifiedName~FrozenOracleEvidenceReaderTests|FullyQualifiedName~CorrectnessServiceCollectionExtensionsTests|FullyQualifiedName~RejectsRealRuntimeRequirementsBeforeAnyProcess|FullyQualifiedName~DoesNotRetryCompilerFailuresEvenIfNextAttemptWouldSucceed'
  filter+='|FullyQualifiedName~Correctness.Corpus|FullyQualifiedName~CompiledCorpusBoundaryTests|FullyQualifiedName~DifferentialCorpusIsolationTests|FullyQualifiedName~EmittedCorpusRunnerTests|FullyQualifiedName~FileCorpusRunnerTests|FullyQualifiedName~EmbeddedCorpus|FullyQualifiedName~RegisteredCorpusRunnerTests|FullyQualifiedName~PatchedCorpusCompilationBuilderTests|FullyQualifiedName~RoslynCorpusCompilerBoundaryTests|FullyQualifiedName~CompilerFailureArtifactWriterTests|FullyQualifiedName~CompilerRejection|FullyQualifiedName~MalformedCompilationRunnerTests|FullyQualifiedName~PropertyRunnerIsolationTests|FullyQualifiedName~RandomCilRunDirectoryFactoryTests|FullyQualifiedName~CompilerRequestRetainsAllRealSourcePathsAndNoFictionalEmittedSource'
  filter+='|FullyQualifiedName~RandomCilCampaignOwnershipTests'
  filter+='|FullyQualifiedName~LinkedCorpusBuildPlanFactoryTests|FullyQualifiedName~LinkedCorpusBuildExecutorTests|FullyQualifiedName~LinkedCorpusObservation|FullyQualifiedName~LinkedCorpusToolPathsProviderTests|FullyQualifiedName~CorpusArtifactFingerprintTests|FullyQualifiedName~LinkedCorpusOracleRunnerTests'
  filter+='|FullyQualifiedName~LinkedCorpusQualificationReceiptWriterTests'
  filter+='|FullyQualifiedName~NonRoslynProfilesFailBeforeFilesOrProcessesAreCreated'
  coverage_types=(
    CfgPropertyRunner CompiledCorpusRunner CompiledCorpusComparisonRunner
    CorpusCompilationCellsSelector CompilerFailureArtifactWriter
    CompilerRejectionFailureWriter CompilerRejectionRunner CompilerRejectionVerifier
    CorpusAssemblyWriter CorpusCaseCatalogBuilder CorpusCaseFixtureFactory
    CorpusCaseManifest CorpusCaseManifestParser CorpusCaseManifestVerifier
    CorpusCaseProfileSelector CorpusCaseTestData CorpusCompilerFailureWriter
    CorpusCompilerProcessVerifier CorpusCompilerResponseParser CorpusCompilerResponseReader
    CorpusCompilerRequestFactory CorpusCompilerRequestWriter CorpusApplicationCompiler
    CorpusMatrixExpander CorpusProfileOverrideParser CorpusReplayCommandFormatter
    CorpusRunDirectoryCleaner CorpusRunDirectoryFactory CorpusSourceArtifactWriter
    CorpusSourceFingerprint CorpusSourceNamesVerifier DifferentialCorpusRunner
    EmbeddedCorpusCaseAssetsReader EmbeddedCorpusSourceReader EmittedCorpusRunner
    FileCorpusRunner GeneratedCilRegressionRunner MalformedCompilationRunner
    PatchedCorpusCompilationBuilder RandomCilRunDirectoryFactory RegisteredCorpusRunner
    RoslynCorpusCompiler CorpusExpectationVerifier CorpusExecutionVerifier
    OracleRuntimeCapabilityVerifier
    LinkedCorpusBuildPlanFactory LinkedCorpusBuildExecutor LinkedCorpusBuildException
    LinkedCorpusServiceCollectionExtensions
    LinkedCorpusObservationProcess LinkedCorpusObservationException
    LinkedCorpusObservationRequestWriter LinkedCorpusObservationResponseParser
    LinkedCorpusObservationResponseReader
    LinkedCorpusToolPathsProvider CorpusArtifactFingerprint
    LinkedCorpusObservationRequestFactory
    LinkedCorpusOracleRunner
    LinkedCorpusQualificationReceiptWriter
  )
  coverage_filter=''
  for coverage_type in "${coverage_types[@]}"; do
    coverage_filter+="${coverage_filter:+%2c}[NetWasm.Compiler.Tests]NetWasm.Compiler.Tests.Correctness.$coverage_type"
  done
  run_gate dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj -c Release \
    --environment NETWASM_CORPUS_PROFILE=Fast \
    --filter "$filter" --logger "trx;LogFileName=$mode.trx" --results-directory "$artifact_dir" \
    /p:CollectCoverage=true /p:IncludeTestAssembly=true \
    "/p:Include=$coverage_filter" \
    /p:Threshold=100 /p:CoverletOutputFormat=cobertura \
    "/p:CoverletOutput=$artifact_dir/support-coverage"
  # The public projection carries these two contracts, not the private tier registry.
  run_gate bash eng/run-semantic-coverage-audit.sh
  run_gate node --test --experimental-test-coverage \
    --test-coverage-include=tests/NetWasm.Compiler.Tests/Correctness/declared-oracle-imports.mjs \
    --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
    tests/NetWasm.Compiler.Tests/Correctness/declared-oracle-imports.test.mjs
  run_gate node --test tests/end-to-end/library-profile-edges/result-contract.test.mjs
  run_gate python3 -B -m unittest eng/tests/test_qualify_sdk_consumers.py \
    eng/tests/test_prepare_wit_bindings_test_tool.py
  run_gate node --test --experimental-test-coverage \
    --test-coverage-include=tests/end-to-end/generated-assembly/cases.mjs \
    --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
    tests/end-to-end/generated-assembly/cases.test.mjs
  run_gate node --test --experimental-test-coverage \
    --test-coverage-include=tests/end-to-end/cache-semantics/cache-contract.mjs \
    --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
    tests/end-to-end/cache-semantics/cache-contract.test.mjs
  run_gate node --test --experimental-test-coverage \
    --test-coverage-include=tests/NetWasm.Compiler.Tests/Correctness/corpus-trace-reader.mjs \
    --test-coverage-include=tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-observer.mjs \
    --test-coverage-include=tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-program.mjs \
    --test-coverage-include=tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-runner.mjs \
    --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
    tests/NetWasm.Compiler.Tests/Correctness/corpus-trace-reader.test.mjs \
    tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-observer.test.mjs \
    tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-program.test.mjs \
    tests/NetWasm.Compiler.Tests/Correctness/linked-corpus-runner.test.mjs
  run_gate node --test --experimental-test-coverage \
    --test-coverage-include=eng/compiler-cases.mjs \
    --test-coverage-include=eng/compiler-case-boundaries.mjs \
    --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 \
    eng/compiler-cases.test.mjs eng/compiler-case-boundaries.test.mjs
else
  export NETWASM_EMSDK_ROOT="${NETWASM_EMSDK_ROOT:-${EMSDK:-${EMSDK_ROOT:-}}}"
  export NETWASM_NODE_PATH="$(command -v node)"
  export NETWASM_WASM_TOOLS_PATH="$(command -v wasm-tools)"
  bash eng/verify-toolchain.sh --qualification
  mkdir "$artifact_dir/linked-receipts"
  export NETWASM_CORPUS_RECEIPTS="$artifact_dir/linked-receipts"
  dotnet build src/NetWasm.CoreLib/NetWasm.CoreLib.csproj -c Release --nologo
  filter='FullyQualifiedName~MathDifferentialTests|FullyQualifiedName~CoreLibScalarCorpusTests|FullyQualifiedName~SortedCollectionsCompatibilityCorpusTests|FullyQualifiedName~ImmutableListCompatibilityCorpusTests|FullyQualifiedName~FrozenOrdinalStringCompatibilityCorpusTests|FullyQualifiedName~ComplexMutableCollectionCompatibilityTests|FullyQualifiedName~StaticProviderCatalogCompilationTests|FullyQualifiedName~CollectionCompatibilityCorpusTests|FullyQualifiedName~ExceptionControlFlowDifferentialTests|FullyQualifiedName~NetWasmOracleRunnerTests|FullyQualifiedName~UnsupportedBoundaryCompilationTests'
  filter+='|FullyQualifiedName~RttiArrayOperationCompilationTests|FullyQualifiedName~MultiFileCorpusTests|FullyQualifiedName~UnsupportedCilBoundaryTests|FullyQualifiedName~CfgPropertyTests|FullyQualifiedName~GeneratedCilRegressionRunnerTests'
  filter+='|FullyQualifiedName~NumericConversionEdgeTests'
  filter+='|FullyQualifiedName~StackTraceCompilationTests'
  filter+='|FullyQualifiedName~FloatingComparisonEdgeTests'
  filter+='|FullyQualifiedName~FloatingArithmeticEdgeTests'
  filter+='|FullyQualifiedName~FloatingKeyEdgeTests'
  filter+='|FullyQualifiedName~DecimalEdgeTests'
  filter+='|FullyQualifiedName~HalfEdgeTests'
  filter+='|FullyQualifiedName~WideIntegerEdgeTests'
  filter+='|FullyQualifiedName~FloatingMathEdgeTests'
  filter+='|FullyQualifiedName~EmittedFloatingComparisonTests'
  filter+='|FullyQualifiedName~IntegerArithmeticEdgeTests'
  filter+='|FullyQualifiedName~NumericTransportEdgeTests'
  filter+='|FullyQualifiedName~RttiCastCorrectnessCompilationTests'
  filter+='|FullyQualifiedName~RttiArraySemanticTests'
  filter+='|FullyQualifiedName~BoxingRepresentationTests'
  filter+='|FullyQualifiedName~DispatchRelationshipTests'
  filter+='|FullyQualifiedName~EmittedDelegateReceiverTests'
  filter+='|FullyQualifiedName~MemoryBoundaryTests'
  filter+='|FullyQualifiedName~ExceptionSequenceTests'
  filter+='|FullyQualifiedName~StaticInitializationTests'
  filter+='|FullyQualifiedName~AsyncLifetimeTests'
  filter+='|FullyQualifiedName~IteratorSequenceTests'
  filter+='|FullyQualifiedName~EncodingStreamBoundaryTests|FullyQualifiedName~TextEncodingCompilationTests|FullyQualifiedName~EncodingContractCompilationTests|FullyQualifiedName~ManagedIoCompatibilityCorpusTests'
  filter+='|FullyQualifiedName~CollectionStateBoundaryTests|FullyQualifiedName~DateRangeBoundaryTests|FullyQualifiedName~DateTimeCompilationTests|FullyQualifiedName~EnvironmentCompilationTests'
  filter+='|FullyQualifiedName~HostInteropCompilationTests|FullyQualifiedName~WitBindingTargetMatrixTests'
  filter+='|FullyQualifiedName~CompilerPreservesFocusedCSharpFifteenRuntimeSemantics|FullyQualifiedName~ModuleInitializerCompilationTests|FullyQualifiedName~ExportInitializationCompilationTests'
  filter+='|FullyQualifiedName~DeterministicAsyncContractDifferentialTests|FullyQualifiedName~EnumerableIteratorContractDifferentialTests|FullyQualifiedName~CleanupOutcomeDifferentialTests|FullyQualifiedName~ControlFlowDifferentialTests'
  # Retained RTTI/representation families accompany the new file/emitted cases.
  filter+='|FullyQualifiedName~RectangularArrayCompilationTests|FullyQualifiedName~ArrayNonGenericCompilationTests|FullyQualifiedName~NullableBoxingCompilationTests|FullyQualifiedName~EnumBoxingCompatibilityCompilationTests|FullyQualifiedName~EnumLayoutInteractionCompilationTests|FullyQualifiedName~BoxedValueCallCompilationTests|FullyQualifiedName~EqualityComparerCompilationTests|FullyQualifiedName~GenericMethodDispatchCompilationTests|FullyQualifiedName~ConstructedGenericCastCompilationTests|FullyQualifiedName~DispatchAndDelegateCompilationTests|FullyQualifiedName~DelegateBindingConversionCompilationTests'
  # Retain earlier memory witnesses; API smoke is not real-collector evidence.
  filter+='|FullyQualifiedName~WideValueStackCompilationTests|FullyQualifiedName~ManagedByReferenceCompilationTests|FullyQualifiedName~NestedScalarByReferenceCompilationTests|FullyQualifiedName~FixedBufferCompilationTests|FullyQualifiedName~UnsafeByReferenceDifferentialTests|FullyQualifiedName~NativeMemoryCompilationTests|FullyQualifiedName~UnsafeMemoryMarshalP11CompatibilityTests|FullyQualifiedName~BufferAndMemoryCompatibilityCompilationTests|FullyQualifiedName~RootFramePreservationCompilationTests|FullyQualifiedName~GcRuntimeTargetMatrixCompilationTests|FullyQualifiedName~MemoryHandleCriticalFinalizerCompatibilityCompilationTests'
  run_gate dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj -c Release \
    --environment NETWASM_CORPUS_PROFILE=Extended \
    --filter "$filter" --logger "trx;LogFileName=$mode.trx" --results-directory "$artifact_dir"
  run_gate node tests/end-to-end/cache-semantics/run.mjs "$artifact_dir/cache-semantics"
  run_gate node tests/end-to-end/generated-assembly/run.mjs "$artifact_dir/generated-assembly" \
    "${NETWASM_GENERATOR_PACKAGE_ROOT:-}" "${NETWASM_LIBRARIES_SOURCE_ROOT:-}"
  # Coverlet's MSBuild report requires a successful VSTest invocation. The full
  # semantic selection above remains independent and includes the RED witnesses.
  facade_filter='FullyQualifiedName~DifferentialHarnessSmokeTests|FullyQualifiedName~MultiFileCorpusTests|FullyQualifiedName~UnsupportedCilBoundaryTests|(FullyQualifiedName~CompilerExecutesDistinctSzAndRankOneArraySemantics&DisplayName~input: 0,)'
  run_gate dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj -c Release \
    --environment NETWASM_CORPUS_PROFILE=Fast \
    --filter "$facade_filter" --logger 'trx;LogFileName=facade-composition.trx' --results-directory "$artifact_dir" \
    /p:CollectCoverage=true /p:IncludeTestAssembly=true \
    '/p:Include=[NetWasm.Compiler.Tests]NetWasm.Compiler.Tests.Correctness.CSharpSemanticTestBase%2c[NetWasm.Compiler.Tests]NetWasm.Compiler.Tests.Correctness.EmittedAssemblyTestBase%2c[NetWasm.Compiler.Tests]NetWasm.Compiler.Tests.Correctness.CompilerRejectionTestBase' \
    /p:Threshold=100 /p:CoverletOutputFormat=cobertura \
    "/p:CoverletOutput=$artifact_dir/facade-coverage"
  run_gate dotnet test tests/NetWasm.Compiler.Wasm.Tests/NetWasm.Compiler.Wasm.Tests.csproj -c Release \
    --filter 'FullyQualifiedName~ComposesTheCompleteTargetFreeModuleGraph|FullyQualifiedName~InstructionCommandRegistryTests' \
    --logger 'trx;LogFileName=instruction-ownership.trx' --results-directory "$artifact_dir"
  run_gate dotnet test tests/NetWasm.Compiler.GarbageCollection.Tests/NetWasm.Compiler.GarbageCollection.Tests.csproj -c Release \
    --filter 'FullyQualifiedName~RootMapTests' \
    --logger 'trx;LogFileName=root-map-contracts.trx' --results-directory "$artifact_dir"
  run_gate node --test \
    src/NetWasm.Hosting/JavaScript/pollable-reactor.test.mjs \
    src/NetWasm.Hosting/JavaScript/execution-lifecycle.test.mjs \
    tests/NetWasm.Runtime.Tests/timer-reactor-imports.test.mjs \
    tests/NetWasm.Runtime.Tests/yield-reactor-imports.test.mjs
  run_gate env NETWASM_INSTANCE_LIFETIME_ARTIFACT_DIR="$artifact_dir/instance-lifetime" \
    bash tests/end-to-end/host-interop/instance-lifetime/run.sh
  run_gate env NETWASM_STRING_COMPONENT_ARTIFACT_DIR="$artifact_dir/string-component" \
    bash tests/end-to-end/component-model/run-string-component.sh
  run_gate env NETWASM_CANONICAL_MATRIX_ARTIFACT_DIR="$artifact_dir/canonical-matrix" \
    bash tests/end-to-end/component-model/run-canonical-matrix.sh
  run_gate env NETWASM_RESOURCE_COMPONENT_ARTIFACT_DIR="$artifact_dir/resource-component" \
    bash tests/end-to-end/component-model/run-resource-component.sh
  run_gate env NETWASM_WASI_STREAM_ARTIFACT_DIR="$artifact_dir/wasi-stream" \
    bash tests/end-to-end/component-model/run-wasi-stream.sh
  run_gate env NETWASM_COMPONENT_MIXED_ARTIFACT_DIR="$artifact_dir/mixed-boundary" \
    bash tests/end-to-end/component-model/run-mixed-boundary.sh
  run_gate env NETWASM_LIBRARY_EDGE_ARTIFACT_DIR="$artifact_dir/library-profile-edges" \
    bash tests/end-to-end/library-profile-edges/run.sh
fi
run_gate git diff --check
exit "$gate_status"
