using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodBodyEmitterTests
{
    [Fact]
    public void BodyEmitterDelegatesToBodyAndSequenceCapabilities()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var roots = new MethodRootMap(ConstructorKey, [], []);
        var target = CreateInstructionModuleTarget(program);
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
        var emitter = new ManagedMethodBodyEmitter(
            methods,
            sequences,
            new StackTraceMethodIdProvider());

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
        Assert.Same(target, sequences.Target);
        Assert.Same(functionIndices, sequences.FunctionIndices);
        Assert.Equal(structured.Body, sequences.Sequence);
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
            new StackTraceMethodIdProvider());

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
        var exceptions = new ImplicitExceptionEmitter(
            layouts,
            layouts,
            imports.Resolve(RuntimeImportSymbol.BeginThrow));
        var exceptionPayloadBlocks = new ExceptionPayloadBlockEmitter(layouts);
        var frameEntry = CreateMethodFrameEntry(program, layouts, imports);
        var frameExit = CreateMethodFrameExit(imports);
        var addresses = CreateAddressInstructions(layouts);
        var returnEmitter = new ExceptionAndReturnEmitter(layouts, layouts, imports,
            exceptions,
            frameExit,
            CreateFilterEnvironmentRoots(layouts),
            addresses);
        var dispatcher = new CilInstructionDispatcher(new InstructionCommandRegistry(
            returnEmitter.Commands,
            [CilOperation.Return]),
            new RecordingRootPublicationEmitter(_ => { }));
        var emitter = new ManagedMethodBodyEmitter(
            new ManagedMethodEmitter(
                layouts,
                new ManagedMethodFunctionTypeResolver(),
                CreateValueFrameLayoutPlanner(program, layouts),
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    types,
                    new ExceptionGroupEnumerator()),
                new ExceptionGroupEnumerator(),
                frameEntry,
                new MethodExceptionBoundaryEmitter(exceptionPayloadBlocks, frameExit),
                new GeneratedFunctionWriterFactory(),
                new InstructionCountingWriterFactory()),
            new ManagedMethodSequenceEmitter(
                layouts,
                new ExceptionRegionEmitter(
                    layouts,
                    imports,
                    exceptionPayloadBlocks,
                    frameExit,
                    new StructuredExceptionGroupKeyFactory()),
                new StructuredControlFlowEmitter(
                    layouts,
                    new ControlFlowDispatcherEmitter()),
                new StructuredLeaveEmitter(),
                new BranchComparisonEmitter(
                    layouts,
                    new NetWasm.Compiler.ControlFlow.StackTypeCompatibilityValidator()),
                dispatcher),
            new StackTraceMethodIdProvider());
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(program, method, I(0, CilOperation.Return));
        var roots = new MethodRootMap(ConstructorKey, [], []);

        var body = emitter.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            roots,
            CreateInstructionModuleTarget(program),
            CreateFunctionIndexResolver(program));

        Assert.NotEmpty(body.Body);
        Assert.True(body.WasmInstructionCount > 0);
        Assert.True(body.CompileDurationTicks > 0);
        Assert.True(body.PeakObservedManagedMemoryBytes > 0);
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

        public ManagedMethodEmission Emit(
MethodDefinitionModel method,
ManagedMethodIdentity callerIdentity,
StructuredMethod structured,
            MethodRootMap rootMap,
            MethodInstanceModel? methodInstance,
            int stackTraceMethodId,
            RuntimeImportSelection runtimeImportSelection,
            Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody)
        {
            Method = method;
            Structured = structured;
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
