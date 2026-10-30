using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodBodyEmitterTests
{
    [Fact]
    public void HeapSamplerReportsTheCurrentManagedHeap()
    {
        Assert.True(new ManagedHeapSampler().Sample() > 0);
    }

    [Fact]
    public void BodyEmitterDelegatesToBodyAndSequenceCapabilities()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var roots = new MethodRootMap(ConstructorKey, [], []);
        var byRefEffects = new ManagedByRefMethodEffects([0], [], []);
        var target = CreateInstructionModuleTarget(program) with
        {
            MemberExecution = MemberExecutionPlan.Empty with { MethodInvokers = [EntryKey] },
            EnableAddressTakenAggregatePromotion = true,
            HoistArrayNullChecks = true,
            LoopRotation = LoopRotationPolicy.Guarded,
            EliminateArrayStoreChecks = true,
            ManagedByRefEffects = new(
                ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty.Add(
                    "Tests.Caller",
                    byRefEffects)),
        };
        var functionIndices = CreateFunctionIndexResolver(program);
        var expectedEnvironment = new FilterEnvironmentLayout(
            8,
            1,
            4,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty);
        var expectedCounts = ImmutableDictionary<int, int>.Empty.Add(4, 2);
        var expected = new ManagedMethodEmission(
            [1, 2, 3],
            expectedEnvironment,
            17);
        var methods = new RecordingMethodEmitter(expected);
        var sequences = new RecordingSequenceEmitter(expectedCounts);
        var heap = new RecordingHeapSampler(100, 200);
        var promotionPlan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(
                0,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            []);
        var promotions = new RecordingPromotionPlanner(promotionPlan);
        var aggregateType = CliTypeIdentity.Named(new("body-tests"), "Tests", "Aggregate", true);
        var aggregatePromotionPlan = AddressTakenAggregatePromotionPlan.Empty with
        {
            PromotedLocals = ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(2,
                AddressTakenAggregateLocalPlan.CreateI4Storage(aggregateType, 8)),
        };
        var aggregatePromotions = new RecordingAggregatePromotionPlanner(
            aggregatePromotionPlan);
        var managedByRefPromotionPlan = ManagedByRefAggregatePromotionPlan.Empty with
        {
            PromotedParameters = ImmutableDictionary<int, ManagedByRefAggregateParameterPlan>.Empty.Add(1,
                new(aggregateType, 8, AddressTakenAggregateLocalPlan.CreateI4Storage(aggregateType, 8).StorageSlots)),
        };
        var managedByRefPromotions = new RecordingManagedByRefPromotionPlanner(
            managedByRefPromotionPlan);
        var aggregateStorage = new ManagedAggregateStorage(
            new(ManagedAggregateStorageKind.Argument, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            sizeof(int),
            true);
        var aggregatePlan = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<
                ManagedAggregateStorageId,
                ManagedAggregateStorage>.Empty.Add(
                    aggregateStorage.Id,
                    aggregateStorage),
        };
        var projectedRoots = new MethodRootMap(ConstructorKey, [], []);
        var managedValues = new RecordingValuePlanBuilder(
            new(projectedRoots, aggregatePlan));
        var emitter = new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider(),
            heap,
            promotions,
            aggregatePromotions,
            managedByRefPromotions,
            managedValues);

        var result = emitter.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            roots,
            target,
            functionIndices);

        Assert.Equal(expected.Body, result.Body);
        Assert.Equal(expected.WasmInstructionCount, result.WasmInstructionCount);
        Assert.Equal(expectedEnvironment, result.FilterEnvironment);
        Assert.Equal(expectedCounts, result.OriginalBlockEmissionCounts);
        Assert.Equal(ConstructorKey.ToString(), result.MethodKey);
        Assert.Same(method, methods.Method);
        Assert.Same(structured, methods.Structured);
        Assert.Same(target.NativeImports, methods.NativeImports);
        Assert.Same(target.MemberExecution, methods.MemberExecution);
        Assert.Same(byRefEffects, methods.ByRefEffects);
        Assert.True(methods.HoistArrayNullChecks);
        Assert.Equal(LoopRotationPolicy.Guarded, methods.LoopRotation);
        Assert.True(methods.EliminateArrayStoreChecks);
        Assert.Same(promotionPlan, methods.AddressTakenScalarPromotions);
        Assert.Same(
            aggregatePromotionPlan,
            methods.AddressTakenAggregatePromotions);
        Assert.Same(
            managedByRefPromotionPlan,
            methods.ManagedByRefAggregatePromotions);
        Assert.Same(structured, promotions.Request!.Method);
        Assert.Equal(new ManagedMethodIdentity("Tests.Caller"), promotions.Request.Caller);
        Assert.Same(target.ManagedCallSites, promotions.Request.CallSites);
        Assert.Same(target.ManagedByRefEffects, promotions.Request.ByRefEffects);
        Assert.False(promotions.Request.Enabled);
        Assert.Same(structured, aggregatePromotions.Request!.Method);
        Assert.Same(target.ManagedCallSites, aggregatePromotions.Request.CallSites);
        Assert.Same(target.ManagedByRefEffects, aggregatePromotions.Request.ByRefEffects);
        Assert.True(aggregatePromotions.Request.Enabled);
        Assert.Same(structured, managedByRefPromotions.Request!.Method);
        Assert.Same(target.ManagedCallSites, managedByRefPromotions.Request.CallSites);
        Assert.True(managedByRefPromotions.Request.Enabled);
        Assert.Same(structured, managedValues.Request!.Method);
        Assert.Same(roots, managedValues.Request.RootMap);
        Assert.Same(target.ManagedByRefEffects, managedValues.Request.ByRefEffects);
        Assert.True(managedValues.Request.Enabled);
        Assert.Equal(2, managedValues.Request.ReservedStorages.Count);
        Assert.Contains(new ManagedAggregateStorageId(ManagedAggregateStorageKind.Local, 2), managedValues.Request.ReservedStorages);
        Assert.Contains(new ManagedAggregateStorageId(ManagedAggregateStorageKind.Argument, 1), managedValues.Request.ReservedStorages);
        Assert.Same(projectedRoots, methods.RootMap);
        Assert.Same(aggregatePlan, methods.ManagedAggregateValues);
        Assert.Same(target, sequences.Target);
        Assert.Same(functionIndices, sequences.FunctionIndices);
        Assert.Equal(structured.Body, sequences.Sequence);
        Assert.Null(result.PeakObservedManagedMemoryBytes);
        Assert.Equal(0, heap.CallCount);
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBodyEmitter(methods, sequences,
            new StackTraceMethodIdProvider(), heap, null!, aggregatePromotions, managedByRefPromotions, managedValues));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider(),
            null!,
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            new RecordingValuePlanBuilder()));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider(),
            heap,
            new AddressTakenScalarPromotionPlanner(),
            null!,
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            new RecordingValuePlanBuilder()));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider(),
            heap,
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            null!,
            new RecordingValuePlanBuilder()));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider(),
            heap,
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            null!));
    }

    [Fact]
    public void BodyEmitterAllowsAnActorToOmitSequenceEmission()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var methods = new RecordingMethodEmitter(
            new([], FilterEnvironmentLayout.Empty, 0),
            invokeBody: false);
        var emitter = new ManagedMethodBodyEmitter(
            methods,
            new RecordingSequenceEmitter([]),
            new StackTraceMethodIdProvider(),
            new RecordingHeapSampler(),
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            new RecordingValuePlanBuilder());

        var result = emitter.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            new MethodRootMap(ConstructorKey, [], []),
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.Empty(result.OriginalBlockEmissionCounts);
    }

    [Fact]
    public void FactoryCreatesIndependentBodyActors()
    {
        var factory = new ManagedMethodBodyEmitterFactory(
            new RecordingMethodEmitter(new([], FilterEnvironmentLayout.Empty, 0)),
            new RecordingSequenceEmitter([]),
            new StackTraceMethodIdProvider(),
            new RecordingHeapSampler(),
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            new RecordingValuePlanBuilder(),
            NullLogger<WasmModuleEmitterFactory>.Instance);

        var first = factory.Create();
        var second = factory.Create();

        Assert.NotSame(first, second);
        Assert.IsType<ManagedMethodBodyDiagnosticDecorator>(first);
        Assert.IsType<ManagedMethodBodyDiagnosticDecorator>(second);
    }

    [Fact]
    public void DiagnosticDecoratorLogsSuccessAndFailureThroughTheLoggerContract()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var roots = new MethodRootMap(ConstructorKey, [], []);
        var target = CreateInstructionModuleTarget(program);
        var functionIndices = CreateFunctionIndexResolver(program);
        var logger = new RecordingLogger<WasmModuleEmitterFactory>();
        var successfulInner = new RecordingBodyEmitter();
        var successful = Assert.IsAssignableFrom<IManagedMethodBodyEmitter>(
            new ManagedMethodBodyDiagnosticDecorator(successfulInner, logger));

        var result = successful.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            roots,
            target,
            functionIndices);

        Assert.Equal("emitted", result.MethodKey);
        Assert.Equal([4300, 4301], logger.Entries.Select(entry => entry.EventId.Id));
        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
        Assert.Equal(1, successfulInner.CallCount);

        logger.Entries.Clear();
        var failure = new InvalidOperationException("expected failure");
        var failing = Assert.IsAssignableFrom<IManagedMethodBodyEmitter>(
            new ManagedMethodBodyDiagnosticDecorator(
                new RecordingBodyEmitter(failure),
                logger));

        var observed = Assert.Throws<InvalidOperationException>(() => failing.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            roots,
            target,
            functionIndices));

        Assert.Same(failure, observed);
        Assert.Equal([4300, 4302], logger.Entries.Select(entry => entry.EventId.Id));
        Assert.Same(failure, logger.Entries[1].Exception);
        Assert.Throws<ArgumentNullException>(() => successful.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            null!,
            roots,
            target,
            functionIndices));
    }

    [Fact]
    public void BodyKeyUsesConstructedIdentityWhenPresent()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var header = new StructuredMethodHeader(method, null, 0, [], [], []);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Constructed", isValueType: false),
            [],
            method.Signature);

        Assert.Equal(ConstructorKey.ToString(), ManagedMethodBodyKey.Resolve(header));
        Assert.Equal(
            instance.CanonicalName,
            ManagedMethodBodyKey.Resolve(header with { MethodInstance = instance }));
        Assert.Throws<ArgumentNullException>(() =>
            ManagedMethodBodyKey.Resolve((StructuredMethodHeader)null!));
    }

    [Fact]
    public void RealMethodBodyEmissionPreservesWasmAndMetrics()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var types = CreateArgumentSignatureTypes(program);
        var exceptions = new ImplicitExceptionEmitter(layouts, layouts);
        var exceptionPayloadBlocks = new ExceptionPayloadBlockEmitter(layouts);
        var frameEntry = CreateMethodFrameEntry(program, layouts, imports);
        var frameExit = CreateMethodFrameExit(imports);
        var addresses = CreateAddressInstructions(layouts);
        var returnEmitter = new ExceptionAndReturnEmitter(layouts, layouts, imports,
            exceptions,
            frameExit,
            CreateFilterEnvironmentRoots(layouts),
            addresses,
            new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts)));
        var dispatcher = new CilInstructionDispatcher(new InstructionCommandRegistry(
            returnEmitter.Commands,
            [CilOperation.Return]),
            new RecordingRootPublicationEmitter(_ => { }),
            new RecordingStackStorageAnchorEmitter(_ => { }));
        var heap = new RecordingHeapSampler(100, 200);
        var emitter = new ManagedMethodBodyEmitter(
            new ManagedMethodEmitter(
                layouts,
                new ManagedMethodFunctionTypeResolver(),
                CreateValueFrameLayoutPlanner(program, layouts),
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    CreateArgumentTypes(program),
                    types,
                    new ExceptionGroupEnumerator()),
                new ExceptionGroupEnumerator(),
                frameEntry,
                new GeneratedFunctionWriterFactory(),
                new InstructionCountingWriterFactory(), new TerminalBranchValueFramePlanner(),
                new ArrayMetadataCachingPlanner(types),
                new ArrayMetadataSnapshotEmitter(layouts, layouts, addresses, new ImplicitExceptionEmitter(layouts, layouts)),
                new ArrayStoreCheckPlanner(types, new UnusedTypeDefinitionResolver()),
                new ScalarCallLifetimePlanner(new ControlFlowGraphBuilderFactory().Create(),
                    new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer())),
                new AuthoritativeRootSourceSelector(), new AuthoritativeRootLayoutPlanner(), new RootClearElisionPlanner(), new RootValueElisionPlanner()),
            new ManagedMethodSequenceEmitter(
                layouts,
                new ExceptionRegionEmitter(
                    layouts,
                    imports,
                    exceptionPayloadBlocks,
                    new StructuredExceptionGroupKeyFactory()),
                new StructuredControlFlowEmitter(
                    new StructuredConditionValueEmitter(layouts),
                    new ControlFlowDispatcherEmitter(
                        new DispatcherBranchTablePlanner()),
                    new ArrayMetadataSnapshotEmitter(layouts, layouts, addresses, new ImplicitExceptionEmitter(layouts, layouts))),
                new StructuredLeaveEmitter(),
                new BranchComparisonEmitter(
                    layouts,
                    new NetWasm.Compiler.ControlFlow.StackTypeCompatibilityValidator()),
                dispatcher,
                new StackTraceFrameLocationEmitter(imports),
                new ManagedAggregateValueInstructionEmitter(
                    layouts,
                    new ValueFrameAddressEmitter(layouts)),
                new ManagedAggregateValueReloadEmitter(
                    layouts,
                    new ValueFrameAddressEmitter(layouts)),
                CreateTerminalBranchValueFrames(program, layouts, imports),
                new ScalarCallPreservationEmitter(layouts, new ValueFrameAddressEmitter(layouts))),
            new StackTraceMethodIdProvider(),
            heap,
            new AddressTakenScalarPromotionPlanner(),
            new RecordingAggregatePromotionPlanner(
                AddressTakenAggregatePromotionPlan.Empty),
            new RecordingManagedByRefPromotionPlanner(
                ManagedByRefAggregatePromotionPlan.Empty),
            new RecordingValuePlanBuilder());
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var roots = new MethodRootMap(ConstructorKey, [], []);

        var body = emitter.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            roots,
            CreateInstructionModuleTarget(program) with
            {
                CollectManagedMethodMemoryMetrics = true,
            },
            CreateFunctionIndexResolver(program));

        Assert.NotEmpty(body.Body);
        Assert.True(body.WasmInstructionCount > 0);
        Assert.True(body.CompileDurationTicks > 0);
        Assert.Equal(200, body.PeakObservedManagedMemoryBytes);
        Assert.Equal(2, heap.CallCount);
        Assert.Equal(1, body.OriginalBlockEmissionCounts[0]);
        Assert.Equal(ConstructorKey.ToString(), body.MethodKey);
        Assert.Same(FilterEnvironmentLayout.Empty, body.FilterEnvironment);
    }

    private sealed class RecordingMethodEmitter(
        ManagedMethodEmission result,
        bool invokeBody = true) :
        IManagedMethodEmitter
    {
        public MethodDefinitionModel? Method { get; private set; }

        public StructuredMethod? Structured { get; private set; }

        public NativeImportPlan? NativeImports { get; private set; }

        public MemberExecutionPlan? MemberExecution { get; private set; }

        public ManagedByRefMethodEffects? ByRefEffects { get; private set; }

        public MethodRootMap? RootMap { get; private set; }

        public AddressTakenScalarPromotionPlan? AddressTakenScalarPromotions
        { get; private set; }

        public AddressTakenAggregatePromotionPlan? AddressTakenAggregatePromotions
        { get; private set; }

        public ManagedByRefAggregatePromotionPlan? ManagedByRefAggregatePromotions
        { get; private set; }

        public ManagedAggregateSsaPlan? ManagedAggregateValues { get; private set; }

        public bool HoistArrayNullChecks { get; private set; }

        public LoopRotationPolicy LoopRotation { get; private set; }

        public bool EliminateArrayStoreChecks { get; private set; }

        public ManagedMethodEmission Emit(
MethodDefinitionModel method,
ManagedMethodIdentity callerIdentity,
StructuredMethod structured,
            MethodRootMap rootMap,
            MethodInstanceModel? methodInstance,
            int stackTraceMethodId,
            RuntimeImportSelection runtimeImportSelection,
            Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody,
            NativeImportPlan? nativeImports = null,
            MemberExecutionPlan? memberExecution = null,
            ManagedByRefMethodEffects? byRefEffects = null,
            NetWasm.Compiler.Wasm.Emission.AddressTakenValues.AddressTakenScalarPromotionPlan?
                addressTakenScalarPromotions = null,
            AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
            ManagedByRefAggregatePromotionPlan? managedByRefAggregatePromotions = null,
            ManagedAggregateSsaPlan? managedAggregateValues = null,
            bool enableBranchValueFramePlacement = false,
            bool enableArrayMetadataCaching = false,
            bool enableScalarCallPreservation = false,
            bool hoistArrayNullChecks = false,
            LoopRotationPolicy loopRotation = LoopRotationPolicy.Disabled,
            bool eliminateArrayStoreChecks = false,
OptionalFunctionIndex managedThrowFunction = default,
            bool inlineSmallBlocks = false)
        {
            Method = method;
            HoistArrayNullChecks = hoistArrayNullChecks;
            LoopRotation = loopRotation;
            EliminateArrayStoreChecks = eliminateArrayStoreChecks;
            Structured = structured;
            NativeImports = nativeImports;
            MemberExecution = memberExecution;
            ByRefEffects = byRefEffects;
            RootMap = rootMap;
            AddressTakenScalarPromotions = addressTakenScalarPromotions;
            AddressTakenAggregatePromotions = addressTakenAggregatePromotions;
            ManagedByRefAggregatePromotions = managedByRefAggregatePromotions;
            ManagedAggregateValues = managedAggregateValues;
            if (invokeBody)
            {
                emitBody(
                    new WasmInstructionWriter(new WasmBinaryWriter(new WasmBinaryBuffer())),
                    structured,
                    CreateMethodEmissionContext());
            }
            return result;
        }
    }

    private sealed class RecordingValuePlanBuilder(
        ManagedMethodValuePlan? result = null) : IManagedMethodValuePlanBuilder
    {
        public ManagedMethodValuePlanRequest? Request { get; private set; }

        public ManagedMethodValuePlan Build(ManagedMethodValuePlanRequest request)
        {
            Request = request;
            return result ?? ManagedMethodValuePlan.Unchanged(request.RootMap);
        }
    }

    private sealed class RecordingPromotionPlanner(
        AddressTakenScalarPromotionPlan result) :
        IAddressTakenScalarPromotionPlanner
    {
        public AddressTakenScalarPromotionRequest? Request { get; private set; }

        public AddressTakenScalarPromotionPlan Plan(
            AddressTakenScalarPromotionRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingAggregatePromotionPlanner(
        AddressTakenAggregatePromotionPlan result) :
        IAddressTakenAggregatePromotionPlanner
    {
        public AddressTakenAggregatePromotionRequest? Request { get; private set; }

        public AddressTakenAggregatePromotionPlan Plan(
            AddressTakenAggregatePromotionRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingManagedByRefPromotionPlanner(
        ManagedByRefAggregatePromotionPlan result) :
        IManagedByRefAggregatePromotionPlanner
    {
        public ManagedByRefAggregatePromotionRequest? Request { get; private set; }

        public ManagedByRefAggregatePromotionPlan Plan(
            ManagedByRefAggregatePromotionRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingSequenceEmitter(
        ImmutableDictionary<int, int> blockCounts) : IManagedMethodSequenceEmitter
    {
        public InstructionModuleTarget? Target { get; private set; }

        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public StructuredSequence? Sequence { get; private set; }

        public ManagedMethodSequenceEmission Emit(
            IWasmInstructionWriter code,
            StructuredMethod method,
            StructuredSequence sequence,
            MethodEmissionContext context,
            InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices,
            int? loopBreakDepth = null,
            int? loopContinueDepth = null,
            int? exceptionLeaveDepth = null)
        {
            Target = target;
            FunctionIndices = functionIndices;
            Sequence = sequence;
            return new(blockCounts);
        }
    }

    private sealed class RecordingBodyEmitter(Exception? failure = null) :
        IManagedMethodBodyEmitter
    {
        public int CallCount { get; private set; }

        public ManagedMethodBodyEmission Emit(
MethodDefinitionModel method,
ManagedMethodIdentity callerIdentity,
StructuredMethod structured,
            MethodRootMap rootMap,
            InstructionModuleTarget target,
            IFunctionIndexResolver functionIndices,
            MethodInstanceModel? methodInstance = null)
        {
            CallCount++;
            if (failure is not null)
            {
                throw failure;
            }

            return new(
                [],
                0,
                0,
                0,
                "emitted",
                FilterEnvironmentLayout.Empty,
                []);
        }
    }

    private sealed class RecordingHeapSampler(params long[] samples) :
        IManagedHeapSampler
    {
        private readonly Queue<long> _samples = new(samples);

        public int CallCount { get; private set; }

        public long Sample()
        {
            CallCount++;
            return _samples.Dequeue();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(eventId, exception));
    }

    private sealed record LogEntry(EventId EventId, Exception? Exception);
}
