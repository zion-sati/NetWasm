using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// xUnit MemberData composition root. Only immutable snapshots survive bootstrap;
// no process, compiler environment, service provider or executable builder is cached.
internal static class CorpusCaseTestData
{
    public const string Math = "math.boundaries";
    public const string EncodingStreams = "text.encoding-streams";
    public const string CollectionState = "collections.state-boundaries";
    public const string NumericPrecision = "numeric.conversion-precision";
    public const string CheckedNonFinite = "numeric.checked-nonfinite";
    public const string FiniteFloating = "numeric.finite-floating-casts";
    public const string UnsignedPrecision = "numeric.unsigned-precision";
    public const string FloatingNarrowing = "numeric.floating-narrowing";
    public const string FloatingComparisons = "numeric.floating-comparisons";
    public const string FloatingArithmetic = "numeric.floating-arithmetic";
    public const string FloatingKeys = "numeric.floating-keys";
    public const string IntegerCasts = "numeric.integer-casts";
    public const string NativeCasts = "numeric.native-casts";
    public const string FloatingNative = "numeric.floating-native-conversions";
    public const string NativeFloating = "numeric.native-floating-conversions";
    public const string FloatingConvertInteger = "numeric.floating-convert-integer";
    public const string FloatingSmallInteger = "numeric.floating-small-integer";
    public const string DecimalEdges = "numeric.decimal-representation-rounding";
    public const string DecimalConversions = "numeric.decimal-integer-conversions";
    public const string DecimalArithmetic = "numeric.decimal-arithmetic";
    public const string DecimalBinary = "numeric.decimal-binary-conversions";
    public const string BinaryDecimal = "numeric.binary-decimal-conversions";
    public const string FixedIntegerArithmetic = "numeric.fixed-integer-arithmetic";
    public const string FixedIntegerShiftsUnary = "numeric.fixed-integer-shifts-unary";
    public const string NativeArithmetic = "numeric.native-integer-arithmetic";
    public const string NativeShiftsUnary = "numeric.native-shifts-unary";
    public const string HalfWidening = "numeric.half-widening";
    public const string HalfNarrowing = "numeric.half-narrowing";
    public const string HalfClassification = "numeric.half-classification";
    public const string WideArithmetic = "numeric.wide-integer-arithmetic";
    public const string WideConversions = "numeric.wide-integer-conversions";
    public const string WideFloating = "numeric.wide-floating-conversions";
    public const string FloatingWide = "numeric.floating-wide-conversions";
    public const string GenericFloatingWide = "numeric.generic-floating-wide";
    public const string GenericWideFloating = "numeric.generic-wide-floating";
    public const string NumericTransport = "numeric.representation-transport";
    public const string ConversionTransport = "numeric.conversion-transport";
    public const string AsyncNumericTransport = "numeric.async-representation-transport";
    public const string AsyncConversionTransport = "numeric.async-conversion-transport";
    public const string FloatingMathExact = "numeric.floating-math-exact";
    public const string FloatingMathDomains = "numeric.floating-math-domains";
    public const string FloatingMathAccuracy = "numeric.floating-math-accuracy";
    public const string ManagedMathFullRange = "numeric.managed-math-full-range";
    public const string BinaryMathEdges = "numeric.binary-math-edges";
    public const string FloatingRounding = "numeric.floating-rounding";
    public const string FloatingRoundingDigits = "numeric.floating-rounding-digits";
    public const string RoundingArguments = "numeric.rounding-arguments";
    public const string EmittedFloatingComparisons = "numeric.emitted-floating-comparisons";
    public const string MultiFile = "harness.multi-file";
    public const string RttiCasts = "rtti.cast-relationships";
    public const string RttiArrays = "rtti.array-operations";
    public const string BoxedRepresentation = "rtti.boxed-representation";
    public const string DispatchRelationships = "rtti.dispatch-relationships";
    public const string ArrayShapes = "rtti.array-shapes";
    public const string GenericRecursion = "rtti.generic-recursion";
    public const string UnsafeBoundaries = "memory.unsafe-boundaries";
    public const string LayoutBoundaries = "memory.layout-boundaries";
    public const string CollectorRetention = "memory.collector-retention";
    public const string CollectorLifecycle = "memory.collector-lifecycle";
    public const string NativeOwnership = "memory.native-ownership";
    public const string ReadonlyCopies = "memory.readonly-copies";
    public const string ExactRootSlots = "memory.exact-root-slots";
    public const string ExceptionOrder = "flow.exception-order";
    public const string StaticInitialization = "flow.static-initialization";
    public const string AsyncLifetime = "flow.async-lifetime";
    public const string ImplicitExceptionLifetime = "flow.implicit-exception-lifetime";
    public const string AsyncEnumeration = "flow.async-enumeration";
    public const string UnwindRoots = "flow.unwind-roots";
    public const string IteratorOrder = "flow.iterator-order";
    public const string ClosedDelegateReceiver = "rtti.delegate-closed-receiver";
    public const string OpenDelegateReceiver = "rtti.delegate-open-receiver";
    public const string NullDelegateReceiver = "rtti.delegate-null-receiver";
    public const string RankOne = "rtti.rank-one-operations";
    public const string ZeroBound = "rtti.rank-one-zero-bound";
    public const string NonZeroBound = "rtti.rank-one-nonzero-address";

    public static CorpusMatrixProfile? ProfileOverride { get; } =
        new CorpusProfileOverrideParser().Parse(Environment.GetEnvironmentVariable("NETWASM_CORPUS_PROFILE"));

    public static CorpusCaseAssets Assets { get; } =
        new EmbeddedCorpusCaseAssetsReader(typeof(CorpusCaseTestData).Assembly).Read();

    public static ImmutableArray<CorpusCaseBinding> Bindings { get; } =
    [
        new(CollectionState, typeof(CollectionStateBoundaryTests).FullName + "." +
            nameof(CollectionStateBoundaryTests.CollisionsMutationComparerFailuresAndViewsPreserveExactState), CorpusInputKind.CSharp),
        new(EncodingStreams, typeof(EncodingStreamBoundaryTests).FullName + "." +
            nameof(EncodingStreamBoundaryTests.IncrementalCodecsAndShortStreamsPreserveCountsStateAndOwnership), CorpusInputKind.CSharp),
        new(AsyncEnumeration, typeof(AsyncLifetimeTests).FullName + "." +
            nameof(AsyncLifetimeTests.AsyncEnumerationPreservesPendingCancellationFailureIdentityAndExactDisposal), CorpusInputKind.CSharp),
        new(IteratorOrder, typeof(IteratorSequenceTests).FullName + "." +
            nameof(IteratorSequenceTests.EnumerationFailuresEarlyExitsAndRepeatedUsePreserveExactCleanupAndIdentity), CorpusInputKind.CSharp),
        new(UnwindRoots, typeof(ExceptionSequenceTests).FullName + "." +
            nameof(ExceptionSequenceTests.RealCollectionPreservesExceptionLocalInteriorAndDisposalRootsDuringUnwind), CorpusInputKind.CSharp),
        new(AsyncLifetime, typeof(AsyncLifetimeTests).FullName + "." +
            nameof(AsyncLifetimeTests.ExplicitSuspensionPreservesRealRootsResultsFailuresCancellationAndCleanup), CorpusInputKind.CSharp),
        new(ImplicitExceptionLifetime, typeof(AsyncLifetimeTests).FullName + "." +
            nameof(AsyncLifetimeTests.ImplicitExceptionsRemainManagedAcrossHeapAndAsyncStorage), CorpusInputKind.CSharp),
        new(StaticInitialization, typeof(StaticInitializationTests).FullName + "." +
            nameof(StaticInitializationTests.InitializationPreservesClosedTypeStateOrderingReentryAndCachedFailure), CorpusInputKind.CSharp),
        new(ExceptionOrder, typeof(ExceptionSequenceTests).FullName + "." +
            nameof(ExceptionSequenceTests.FilterSearchUnwindAndCleanupPreserveExactEventsAndExceptionIdentity), CorpusInputKind.CSharp),
        new(ExactRootSlots, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.RealCollectionDistinguishesManagedReferenceSlotsFromOpaqueAddressBits), CorpusInputKind.CSharp),
        new(NativeOwnership, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.RealNativeAllocationsPreserveAlignmentContentsAndOwnership), CorpusInputKind.CSharp),
        new(ReadonlyCopies, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.ReadonlyAccessDefensivelyCopiesWhileWritableReferencesMutateTheirOwners), CorpusInputKind.CSharp),
        new(CollectorRetention, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.RealCollectionPreservesLiveRootsInteriorReferencesAndOwnedHandles), CorpusInputKind.CSharp),
        new(CollectorLifecycle, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.RealCollectionClearsDeadWeakReferencesAndHonorsFinalizerLifecycle), CorpusInputKind.CSharp),
        new(LayoutBoundaries, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.PackedExplicitNestedAndGenericLayoutsPreserveOffsetsValuesAndAliases), CorpusInputKind.CSharp),
        new(UnsafeBoundaries, typeof(MemoryBoundaryTests).FullName + "." +
            nameof(MemoryBoundaryTests.ValidUnsafeAndSpanOperationsPreserveBoundsAliasingAndFailureState), CorpusInputKind.CSharp),
        new(GenericRecursion, typeof(DispatchRelationshipTests).FullName + "." +
            nameof(DispatchRelationshipTests.RecursiveAndVirtualGenericCallsPreserveClosedSubstitutionsAndState), CorpusInputKind.CSharp),
        new(ClosedDelegateReceiver, typeof(EmittedDelegateReceiverTests).FullName + "." +
            nameof(EmittedDelegateReceiverTests.ClosedInstanceDelegateKeepsItsCapturedReceiver), CorpusInputKind.Emitted),
        new(OpenDelegateReceiver, typeof(EmittedDelegateReceiverTests).FullName + "." +
            nameof(EmittedDelegateReceiverTests.OpenInstanceDelegateTakesItsReceiverFromTheInvokeArgument), CorpusInputKind.Emitted),
        new(NullDelegateReceiver, typeof(EmittedDelegateReceiverTests).FullName + "." +
            nameof(EmittedDelegateReceiverTests.OpenInstanceDelegateWithNullReceiverThrowsManagedNullReference), CorpusInputKind.Emitted),
        new(ArrayShapes, typeof(RttiArraySemanticTests).FullName + "." +
            nameof(RttiArraySemanticTests.RectangularAndJaggedArraysPreserveShapeCovarianceAndFailureState), CorpusInputKind.CSharp),
        new(DispatchRelationships, typeof(DispatchRelationshipTests).FullName + "." +
            nameof(DispatchRelationshipTests.ExactVariantConstrainedAndDelegateCallsPreserveSlotsAndState), CorpusInputKind.CSharp),
        new(BoxedRepresentation, typeof(BoxingRepresentationTests).FullName + "." +
            nameof(BoxingRepresentationTests.NullableEnumAndReferenceStructBoxesPreserveValueIdentityAndMutation), CorpusInputKind.CSharp),
        new(RttiArrays, typeof(RttiArraySemanticTests).FullName + "." +
            nameof(RttiArraySemanticTests.CompilerPreservesChecksAfterArrayConversions), CorpusInputKind.CSharp),
        new(RttiCasts, typeof(RttiCastCorrectnessCompilationTests).FullName + "." +
            nameof(RttiCastCorrectnessCompilationTests.CompilerPreservesRuntimeCastAndArrayAssignmentSemantics), CorpusInputKind.CSharp),
        new(FloatingSmallInteger, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.SmallIntegerCastsTruncateBeforeCheckingDestinationRange), CorpusInputKind.CSharp),
        new(AsyncConversionTransport, typeof(NumericTransportEdgeTests).FullName + "." +
            nameof(NumericTransportEdgeTests.AsyncConversionsPreserveSeparateSourceFloatingAndRoundTripContracts), CorpusInputKind.CSharp),
        new(AsyncNumericTransport, typeof(NumericTransportEdgeTests).FullName + "." +
            nameof(NumericTransportEdgeTests.AsyncSuspensionPreservesNumericRepresentationsAndNativeWidth), CorpusInputKind.CSharp),
        new(ConversionTransport, typeof(NumericTransportEdgeTests).FullName + "." +
            nameof(NumericTransportEdgeTests.ConversionBoundariesPreserveIndependentSourceFloatingAndRoundTripObservations), CorpusInputKind.CSharp),
        new(FloatingConvertInteger, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.ConvertRoundsToEvenBeforeCheckingIntegerRanges), CorpusInputKind.CSharp),
        new(FloatingRoundingDigits, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.DigitRoundingPreservesFractionalTiesAndLargeValueBoundaries), CorpusInputKind.CSharp),
        new(NativeFloating, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.NativeInputsRespectWidthSpecificFloatingRounding), CorpusInputKind.CSharp),
        new(FloatingNative, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.FloatingInputsRespectNativeWidthTruncationAndOverflow), CorpusInputKind.CSharp),
        new(FloatingMathAccuracy, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.FiniteFunctionsStayWithinDeclaredReferenceErrorBudget), CorpusInputKind.CSharp),
        new(ManagedMathFullRange, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.ManagedTranscendentalsPreserveFullRangeAndSpecialValues), CorpusInputKind.CSharp),
        new(NumericTransport, typeof(NumericTransportEdgeTests).FullName + "." +
            nameof(NumericTransportEdgeTests.NumericRepresentationsSurviveStorageAndCallBoundaries), CorpusInputKind.CSharp),
        new(GenericWideFloating, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.GenericWideInputsRespectFloatingRoundingForEveryPolicy), CorpusInputKind.CSharp),
        new(GenericFloatingWide, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.GenericFloatingInputsRespectCheckedSaturatingAndTruncatingPolicies), CorpusInputKind.CSharp),
        new(FloatingWide, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.FloatingInputsRespectWideTruncationSaturationAndCheckedRanges), CorpusInputKind.CSharp),
        new(WideFloating, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.FloatingCastsRespectNet10RoundingAndOverflowBoundaries), CorpusInputKind.CSharp),
        new(BinaryMathEdges, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.BinaryFunctionsRespectSpecialValuePrecedenceAndQuadrants), CorpusInputKind.CSharp),
        new(FloatingMathDomains, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.DomainEdgesPreserveNaNInfinityAndSignedZeroContracts), CorpusInputKind.CSharp),
        new(NativeShiftsUnary, typeof(IntegerArithmeticEdgeTests).FullName + "." +
            nameof(IntegerArithmeticEdgeTests.NativeShiftsAndUnaryOperationsUseTargetWidthResultsAndState), CorpusInputKind.CSharp),
        new(NativeArithmetic, typeof(IntegerArithmeticEdgeTests).FullName + "." +
            nameof(IntegerArithmeticEdgeTests.NativeArithmeticUsesTargetWidthValueAndExceptionContracts), CorpusInputKind.CSharp),
        new(FixedIntegerShiftsUnary, typeof(IntegerArithmeticEdgeTests).FullName + "." +
            nameof(IntegerArithmeticEdgeTests.ShiftsAndUnaryOperationsPreserveMaskedCountsResultsAndState), CorpusInputKind.CSharp),
        new(FixedIntegerArithmetic, typeof(IntegerArithmeticEdgeTests).FullName + "." +
            nameof(IntegerArithmeticEdgeTests.FixedWidthOperationsRespectWrappingOverflowAndDivisionContracts), CorpusInputKind.CSharp),
        new(BinaryDecimal, typeof(DecimalEdgeTests).FullName + "." +
            nameof(DecimalEdgeTests.FloatingInputsRespectDecimalRangeUnderflowAndPrecision), CorpusInputKind.CSharp),
        new(DecimalBinary, typeof(DecimalEdgeTests).FullName + "." +
            nameof(DecimalEdgeTests.BinaryConversionsRespectExactRoundingAndSignedZero), CorpusInputKind.CSharp),
        new(EmittedFloatingComparisons, typeof(EmittedFloatingComparisonTests).FullName + "." +
            nameof(EmittedFloatingComparisonTests.OrderedAndUnorderedInstructionsRespectNaNAndNumericOrdering), CorpusInputKind.Emitted),
        new(DecimalArithmetic, typeof(DecimalEdgeTests).FullName + "." +
            nameof(DecimalEdgeTests.ArithmeticRespectsRoundingOverflowAndDivisionContracts), CorpusInputKind.CSharp),
        new(RoundingArguments, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.RoundingArgumentsRespectNet10LimitsAndExceptionPrecedence), CorpusInputKind.CSharp),
        new(FloatingRounding, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.RoundingModesRespectHalfwayNeighboursAndSignedZero), CorpusInputKind.CSharp),
        new(FloatingMathExact, typeof(FloatingMathEdgeTests).FullName + "." +
            nameof(FloatingMathEdgeTests.ExactMathOperationsRespectNaNZeroAndAdjacentValueContracts), CorpusInputKind.CSharp),
        new(WideConversions, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.CastsAndGenericConversionsPreserveDistinctOverflowPolicies), CorpusInputKind.CSharp),
        new(WideArithmetic, typeof(WideIntegerEdgeTests).FullName + "." +
            nameof(WideIntegerEdgeTests.ArithmeticAndShiftsRespect128BitValueAndExceptionContracts), CorpusInputKind.CSharp),
        new(HalfClassification, typeof(HalfEdgeTests).FullName + "." +
            nameof(HalfEdgeTests.ClassificationAndZeroComparisonsExecuteIndependently), CorpusInputKind.CSharp),
        new(HalfNarrowing, typeof(HalfEdgeTests).FullName + "." +
            nameof(HalfEdgeTests.NarrowingRespectsHalfwayRoundingAndExceptionalValues), CorpusInputKind.CSharp),
        new(HalfWidening, typeof(HalfEdgeTests).FullName + "." +
            nameof(HalfEdgeTests.WideningRoundTripsAndClassificationPreserveBinary16Contracts), CorpusInputKind.CSharp),
        new(DecimalConversions, typeof(DecimalEdgeTests).FullName + "." +
            nameof(DecimalEdgeTests.IntegerConversionsDistinguishTruncationRoundingAndOverflow), CorpusInputKind.CSharp),
        new(DecimalEdges, typeof(DecimalEdgeTests).FullName + "." +
            nameof(DecimalEdgeTests.RepresentationAndRoundingRespectTheirDistinctContracts), CorpusInputKind.CSharp),
        new(NativeCasts, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.NativeCastsRespectTargetWidthAndCheckedOverflow), CorpusInputKind.CSharp),
        new(IntegerCasts, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.IntegerCastsRespectSignednessWidthAndOverflow), CorpusInputKind.CSharp),
        new(FloatingKeys, typeof(FloatingKeyEdgeTests).FullName + "." +
            nameof(FloatingKeyEdgeTests.ComparersAndCollectionsRespectFloatingKeyEquivalence), CorpusInputKind.CSharp),
        new(FloatingArithmetic, typeof(FloatingArithmeticEdgeTests).FullName + "." +
            nameof(FloatingArithmeticEdgeTests.RuntimeArithmeticPreservesExceptionalValuesRoundingAndZeroSigns), CorpusInputKind.CSharp),
        new(FloatingComparisons, typeof(FloatingComparisonEdgeTests).FullName + "." +
            nameof(FloatingComparisonEdgeTests.OperatorsBranchesEqualsAndCompareToRespectTheirDistinctContracts), CorpusInputKind.CSharp),
        new(FloatingNarrowing, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.FloatingNarrowingPreservesRoundingClassificationAndZeroSigns), CorpusInputKind.CSharp),
        new(UnsignedPrecision, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.UnsignedConversionsPreserveRoundingAndCheckedRoundTripBounds), CorpusInputKind.CSharp),
        new(FiniteFloating, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.FiniteFloatingConversionsRespectTruncationAndRange), CorpusInputKind.CSharp),
        new(NumericPrecision, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.ConversionsPreserveSpecifiedPrecisionAndRoundTripResults), CorpusInputKind.CSharp),
        new(CheckedNonFinite, typeof(NumericConversionEdgeTests).FullName + "." +
            nameof(NumericConversionEdgeTests.CheckedNonFiniteConversionsThrowManagedOverflow), CorpusInputKind.CSharp),
        new(Math, typeof(MathDifferentialTests).FullName + "." +
            nameof(MathDifferentialTests.MathAndMathFMatchTheDesktopOracleAtBoundaryAndRepresentativeInputs), CorpusInputKind.CSharp),
        new(MultiFile, typeof(MultiFileCorpusTests).FullName + "." +
            nameof(MultiFileCorpusTests.SeparateSourceFilesPreserveTheirFileLocalTypesAndScopes), CorpusInputKind.CSharp),
        new(RankOne, typeof(RttiArrayOperationCompilationTests).FullName + "." +
            nameof(RttiArrayOperationCompilationTests.CompilerExecutesDistinctSzAndRankOneArraySemantics), CorpusInputKind.Emitted),
        new(ZeroBound, typeof(RttiArrayOperationCompilationTests).FullName + "." +
            nameof(RttiArrayOperationCompilationTests.CompilerPreservesZeroBasedRankOneArrayIdentity), CorpusInputKind.Emitted),
        new(NonZeroBound, typeof(RttiArrayOperationCompilationTests).FullName + "." +
            nameof(RttiArrayOperationCompilationTests.CompilerPreservesMutableAddressForNonZeroBoundRankOneArray), CorpusInputKind.Emitted),
    ];

    public static CorpusCaseCatalog Catalog { get; } = new CorpusCaseCatalogBuilder(
        new CorpusCaseManifestParser(),
        new CorpusCaseManifestVerifier(Assets.Features, new CorpusSourceNamesVerifier(),
            new CorpusMatrixExpander(), new CorpusReplayCommandFormatter())).Build(Assets, Bindings);

    public static CorpusCaseManifest Get(string caseId) =>
        new CorpusCaseProfileSelector(new CorpusMatrixExpander()).Select(Catalog.Cases[caseId], ProfileOverride);

    public static KnownNonBugCorpusPartition Partition(string caseId, string cellId)
    {
        var manifest = Get(caseId);
        var cell = new CorpusMatrixExpander().Expand(manifest.InputKind,
            new(manifest.MatrixProfile, cellId, manifest.ExecutionBackend)).Single();
        return KnownNonBugCorpusPartitioner.Partition(manifest, cell);
    }

    public static CorpusCaseManifest Active(string caseId, string cellId) =>
        Partition(caseId, cellId).ActiveManifest;

    public static TheoryData<string, string> Cells(string caseId)
    {
        var manifest = Get(caseId);
        var rows = new TheoryData<string, string>();
        foreach (var cell in new CorpusMatrixExpander().Expand(manifest.InputKind, new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend)))
        {
            rows.Add(manifest.CaseId, cell.Id);
        }
        return rows;
    }

    public static TheoryData<int, string, string> InputCells(string caseId)
    {
        var manifest = Get(caseId);
        var rows = new TheoryData<int, string, string>();
        foreach (var input in manifest.Inputs)
        {
            foreach (var cell in new CorpusMatrixExpander().Expand(manifest.InputKind, new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend)))
            {
                rows.Add(input, manifest.CaseId, cell.Id);
            }
        }
        return rows;
    }

    public static TheoryData<int, string, string> ActiveInputCells(string caseId)
    {
        var manifest = Get(caseId);
        var rows = new TheoryData<int, string, string>();
        foreach (var cell in new CorpusMatrixExpander().Expand(manifest.InputKind,
                     new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend)))
        {
            foreach (var input in KnownNonBugCorpusPartitioner.Partition(manifest, cell)
                         .ActiveManifest.Inputs)
            {
                rows.Add(input, manifest.CaseId, cell.Id);
            }
        }
        return rows;
    }
}
