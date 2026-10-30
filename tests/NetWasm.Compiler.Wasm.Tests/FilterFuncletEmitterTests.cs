using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FilterFuncletEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MemberExecutionPlanPreservesScratchOffsetsInsideTheParentEnvironment(WasmTarget target)
    {
        var program = new FakeProgram();
        var method = CreateFilterMethod(program);
        var plan = MemberExecutionPlan.Empty with { MethodInvokers = [EntryKey] };
        var scratch = new ValueFrameLayout(32, [], [], [], [])
        {
            MemberResultOffsets = ImmutableDictionary<int, int>.Empty.Add(7, 8),
        };
        var valueFrames = new RecordingValueFramePlanner(scratch);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = ThroughContract(new FilterFuncletEmitter(
            layouts, valueFrames, new ExceptionPayloadBlockEmitter(layouts),
            new GeneratedFunctionWriterFactory()));
        var environment = new FilterEnvironmentLayout(64, 48, 0, []);
        MethodEmissionContext? emittedContext = null;
        var sequence = new FilterSequenceEmitterAdapter((_, _, _, context) =>
        {
            emittedContext = context;
            return new ManagedMethodSequenceEmission([]);
        });

        var emission = emitter.Emit(
            new FilterFunclet(1, new ManagedMethodIdentity("test-filter"), method,
                CreateFilterClause(StructuredSequence.Empty)),
            environment,
            EmptyRootMap(),
            sequence,
            plan);

        Assert.Same(plan, valueFrames.MemberExecution);
        Assert.Same(method, valueFrames.Method);
        Assert.NotNull(emittedContext);
        Assert.Equal(8, emittedContext.ValueLayout.MemberResultOffsets[7]);
        Assert.Equal(scratch with { Size = environment.Size }, emittedContext.ValueLayout);
        Assert.Equal(1, emittedContext.ValueFrame);
        Assert.Same(environment, emittedContext.FilterEnvironment);
        Assert.NotEmpty(emission.Body);
    }

    [Fact]
    public void SequenceAdapterRejectsAMissingEmissionCapability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FilterSequenceEmitterAdapter(null!));
    }

    [Fact]
    public void FuncletUsesIsolatedContextAndTargetWidth()
    {
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var clause = new StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            0,
            1,
            null,
            0,
            StructuredSequence.Empty,
            StructuredSequence.Empty)
        {
            HandlerBlock = method.EntryBlock,
            FilterBlock = method.EntryBlock,
        };
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        MethodEmissionContext? context = null;
        var sequence = new FilterSequenceEmitterAdapter(
            (_, _, _, emittedContext) =>
            {
                context = emittedContext;
                return new ManagedMethodSequenceEmission(
                    ImmutableDictionary<int, int>.Empty.Add(0, 1));
            });

        var emitter = new FilterFuncletEmitter(
layouts,
            CreateValueFrameLayoutPlanner(program, layouts),
            new ExceptionPayloadBlockEmitter(layouts),
            new GeneratedFunctionWriterFactory());
        var emission = ((IFilterFuncletEmitter)emitter).Emit(
                    new FilterFunclet(1,
            new ManagedMethodIdentity("test-filter"), method, clause),
                    new FilterEnvironmentLayout(
                        16,
                        0,
                        8,
                        []),
                    EmptyRootMap(),
                    sequence);

        Assert.NotNull(context);
        Assert.True(context.IsFilterFunclet);
        Assert.Equal(new ManagedMethodIdentity("test-filter"), context.CallerIdentity);
        Assert.Contains(WasmOpcodes.I64Constant, emission.Body);
        Assert.Contains(WasmOpcodes.I64Load, emission.Body);
        Assert.Contains(WasmOpcodes.TryTable, emission.Body);
        Assert.Equal(1, emission.OriginalBlockEmissionCounts[0]);
    }

    [Fact]
    public void FuncletDeclaresEveryTemporaryItsContextNames()
    {
        var program = new FakeProgram();
        var method = CreateFilterMethod(program);
        MethodEmissionContext? context = null;
        var sequence = new FilterSequenceEmitterAdapter(
            (_, _, _, emittedContext) =>
            {
                context = emittedContext;
                return new ManagedMethodSequenceEmission(
                    ImmutableDictionary<int, int>.Empty.Add(0, 1));
            });

        CreateEmitter(WasmTarget.Wasm32).Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                method,
                CreateFilterClause(StructuredSequence.Empty)),
            CreateEnvironment(rootFrameOffset: 0),
            EmptyRootMap(),
            sequence);

        // The funclet's two parameters precede its declared locals.
        var locals = WasmLocalLayoutPlanner.CreateFilterLocals(method.Header);
        CliValueKind Declared(int index) => locals[index - 2];
        Assert.NotNull(context);
        Assert.Equal(CliValueKind.ManagedAddress, Declared(context.InteropDescriptor));
        Assert.Equal(CliValueKind.I4, Declared(context.InteropResult));
        Assert.Equal(CliValueKind.I4, Declared(context.InteropHandle));
        Assert.Equal(CliValueKind.I4, Declared(context.NumericTemporaryI4));
        Assert.Equal(CliValueKind.I8, Declared(context.NumericTemporaryI8));
        Assert.Equal(CliValueKind.I4, Declared(context.DispatcherProgramCounter));
        Assert.Equal(CliValueKind.I4, Declared(context.NumericTemporaryI4Second));
        Assert.Equal(CliValueKind.I4, Declared(context.InlineCatchFrame));
        Assert.Equal(locals.Length + 1, context.InlineCatchFrame);
        Assert.NotEqual(context.NumericTemporaryI4, context.DispatcherProgramCounter);
    }

    [Fact]
    public void Memory32AtRootFrameBaseUses32BitAddressInstructions()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        var emission = emitter.Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                CreateFilterMethod(program),
                CreateFilterClause(StructuredSequence.Empty)),
            CreateEnvironment(rootFrameOffset: 0),
            EmptyRootMap(),
            CreateSequence());

        Assert.Contains(WasmOpcodes.I32Load, emission.Body);
        Assert.Contains(WasmOpcodes.I32Constant, emission.Body);
    }

    [Fact]
    public void Memory32AddsNonZeroRootFrameOffset()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        var emission = emitter.Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                CreateFilterMethod(program),
                CreateFilterClause(StructuredSequence.Empty)),
            CreateEnvironment(rootFrameOffset: 8),
            EmptyRootMap(),
            CreateSequence());

        Assert.Contains(WasmOpcodes.I32Add, emission.Body);
        Assert.Contains(WasmOpcodes.I32Load, emission.Body);
    }

    [Fact]
    public void LoadsParentRootFrameAndKeepsEvaluationAnchorsFunctionLocal()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var writers = new RecordingWriterFactory();
        var backingType = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Backing",
            isValueType: true);
        var rootMap = new MethodRootMap(EntryKey, [], [])
        {
            AnchorPlan = new(
            [
                new(new(StackStorageLocationKind.EvaluationStack, 0), backingType),
                new(new(StackStorageLocationKind.Local, 0), backingType),
            ],
            []),
        };
        var environment = new FilterEnvironmentLayout(20, 0, 4, [])
        {
            ParentRootFrameOffset = 12,
            AnchorOffsets = ImmutableDictionary<int, int>.Empty.Add(1, 8),
        };
        MethodEmissionContext? context = null;
        var sequence = new FilterSequenceEmitterAdapter((_, _, _, emittedContext) =>
        {
            context = emittedContext;
            return new ManagedMethodSequenceEmission([]);
        });
        var emitter = new FilterFuncletEmitter(
            layouts,
            CreateValueFrameLayoutPlanner(program, layouts),
            new ExceptionPayloadBlockEmitter(layouts),
            writers);

        ((IFilterFuncletEmitter)emitter).Emit(
            new FilterFunclet(
                1,
                new ManagedMethodIdentity("test-filter"),
                CreateFilterMethod(program),
                CreateFilterClause(StructuredSequence.Empty)),
            environment,
            rootMap,
            sequence);

        Assert.NotNull(context);
        Assert.Same(rootMap, context.RootMap);
        var anchor = Assert.Single(context.AnchorLocals);
        Assert.Equal(0, anchor.Key);
        Assert.DoesNotContain(1, context.AnchorLocals.Keys);
        var instructions = writers.Instructions.ToInstructions();
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Load));
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == context.RootFrame);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand.SignedValue == environment.ParentRootFrameOffset);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        var program = new FakeProgram();
        var method = CreateFilterMethod(program);
        var filter = new FilterFunclet(
            1,
            new ManagedMethodIdentity("test-filter"),
            method,
            CreateFilterClause(StructuredSequence.Empty));
        var environment = CreateEnvironment(rootFrameOffset: 0);
        var sequence = CreateSequence();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(null!, environment, EmptyRootMap(), sequence));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(filter, null!, EmptyRootMap(), sequence));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(filter, environment, null!, sequence));
        Assert.Throws<ArgumentNullException>(() =>
            emitter.Emit(filter, environment, EmptyRootMap(), null!));
    }

    [Fact]
    public void RejectsFilterWithoutFilterBody()
    {
        var program = new FakeProgram();
        var emitter = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(
                new FilterFunclet(
                    1,
                    new ManagedMethodIdentity("test-filter"),
                    CreateFilterMethod(program),
                    CreateFilterClause(filterBody: null)),
                CreateEnvironment(rootFrameOffset: 0),
                EmptyRootMap(),
                CreateSequence()));
    }

    private static IFilterFuncletEmitter ThroughContract(IFilterFuncletEmitter emitter) => emitter;

    private static IFilterFuncletEmitter CreateEmitter(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var program = new FakeProgram();
        return new[]
        {
            new FilterFuncletEmitter(
layouts,
                CreateValueFrameLayoutPlanner(program, layouts),
                new ExceptionPayloadBlockEmitter(layouts),
                new GeneratedFunctionWriterFactory()),
        }.Cast<IFilterFuncletEmitter>().Single();
    }

    private sealed class RecordingValueFramePlanner(ValueFrameLayout result) : IValueFrameLayoutPlanner
    {
        public MemberExecutionPlan? MemberExecution { get; private set; }
        public StructuredMethod? Method { get; private set; }

        public ValueFrameLayout Create(
            StructuredMethod method,
            NativeImportPlan? nativeImports = null,
            MemberExecutionPlan? memberExecution = null,
            ManagedByRefMethodEffects? byRefEffects = null,
            NetWasm.Compiler.Wasm.Emission.AddressTakenValues.AddressTakenScalarPromotionPlan?
                addressTakenScalarPromotions = null,
            NetWasm.Compiler.Wasm.Emission.AddressTakenValues.AddressTakenAggregatePromotionPlan?
                addressTakenAggregatePromotions = null,
            NetWasm.Compiler.Wasm.Emission.CallLifetimes.ScalarCallLifetimePlan?
                scalarCallLifetimes = null)
        {
            Method = method;
            MemberExecution = memberExecution;
            return result;
        }
    }

    private static StructuredMethod CreateFilterMethod(FakeProgram program) =>
        Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

    private static StructuredExceptionClause CreateFilterClause(
        StructuredSequence? filterBody) =>
        new(
            CilExceptionRegionKind.Filter,
            0,
            1,
            null,
            filterBody is null ? null : 0,
            StructuredSequence.Empty,
            filterBody)
        {
            HandlerBlock = new StructuredBlockId(0),
            FilterBlock = filterBody is null ? null : new StructuredBlockId(0),
        };

    private static FilterEnvironmentLayout CreateEnvironment(int rootFrameOffset) =>
        new(16, rootFrameOffset, 8, []);

    private static MethodRootMap EmptyRootMap() => new(EntryKey, [], []);

    private static FilterSequenceEmitterAdapter CreateSequence() =>
        new FilterSequenceEmitterAdapter(
            (_, _, _, _) => new ManagedMethodSequenceEmission(
                ImmutableDictionary<int, int>.Empty.Add(0, 1)));

    private sealed class RecordingWriterFactory : IGeneratedFunctionWriterFactory
    {
        public RecordingInstructionWriter Instructions { get; } = new();

        public GeneratedFunctionWriterLease Create()
        {
            var buffer = new WasmBinaryBuffer();
            return new(
                new WasmBinaryWriter(buffer),
                new WasmBinarySnapshotReader(buffer),
                Instructions);
        }
    }
}
