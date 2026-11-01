using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedMethodEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BranchPlacementReservesScalarShadowsButDefersTheValueFrame(bool enabled)
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = Structure(program, method, I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return));
        structured = structured with
        {
            Header = structured.Header with
            {
                Locals = [CliValueKind.I8],
                LocalSignatureTypes = [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
            }
        };
        var layout = new ValueFrameLayout(16, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], [0]);
        var plan = new TerminalBranchValueFramePlan(new(new(0), StructuredBlockRole.Owner),
            StructuredSequence.Empty, StructuredSequence.Empty, true, [0]);
        var placement = new RecordingBranchPlacement(plan);
        var frameEntry = new RecordingFrameEntry();
        var emitter = ThroughContract(CreateEmitter(program, new RecordingValueFramePlanner(layout), placement, frameEntry));
        MethodEmissionContext? bodyContext = null;

        emitter.Emit(method, new("Tests.Caller"), structured, new(EntryKey, [], []), null, 0, default,
            (code, _, context) =>
            {
                bodyContext = context;
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            }, enableBranchValueFramePlacement: enabled);

        Assert.NotNull(bodyContext);
        Assert.Equal(enabled ? 1 : 0, placement.Calls);
        Assert.Equal(enabled ? 0 : layout.Size, frameEntry.Context!.ValueLayout.Size);
        Assert.Equal(enabled ? 0 : layout.Size, bodyContext.ValueLayout.Size);
        if (enabled)
        {
            Assert.NotNull(bodyContext.TerminalBranchValueFramePlacement);
            Assert.Same(plan, bodyContext.TerminalBranchValueFramePlacement.Plan);
            Assert.Equal(layout, bodyContext.TerminalBranchValueFramePlacement.FramedLayout);
            Assert.Contains(0, bodyContext.AddressTakenScalarPromotions.ShadowLocals.Keys);
            Assert.NotEqual(bodyContext.LocalBase, bodyContext.AddressTakenScalarPromotions.ShadowLocals[0]);
            Assert.Empty(bodyContext.ValueLayout.SpilledScalarLocals);
        }
        else
        {
            Assert.Null(bodyContext.TerminalBranchValueFramePlacement);
            Assert.Empty(bodyContext.AddressTakenScalarPromotions.ShadowLocals);
            Assert.Contains(0, bodyContext.ValueLayout.SpilledScalarLocals);
        }
    }

    [Fact]
    public void MemberExecutionPlanReachesFramePlanningAndPreservesItsResultOffsets()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = Structure(program, method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var plan = MemberExecutionPlan.Empty with { MethodInvokers = [EntryKey] };
        var expectedLayout = new ValueFrameLayout(32, [], [], [], [])
        {
            MemberResultOffsets = ImmutableDictionary<int, int>.Empty.Add(7, 8),
        };
        var valueFrames = new RecordingValueFramePlanner(expectedLayout);
        var emitter = ThroughContract(CreateEmitter(program, valueFrames));
        MethodEmissionContext? emittedContext = null;

        var emission = emitter.Emit(
            method,
            new ManagedMethodIdentity("Tests.Caller"),
            structured,
            new MethodRootMap(EntryKey, [], []),
            null,
            0,
            new RuntimeImportSelection(WasmModuleProfile.CoreApplication,
                IncludeTerminalExceptionReporter: true),
            (code, _, context) =>
            {
                emittedContext = context;
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            },
            memberExecution: plan);

        Assert.Same(plan, valueFrames.MemberExecution);
        Assert.Equal(structured, valueFrames.Method);
        Assert.NotNull(emittedContext);
        Assert.Equal(expectedLayout, emittedContext.ValueLayout);
        Assert.Equal(8, emittedContext.ValueLayout.MemberResultOffsets[7]);
        Assert.Equal(32, emittedContext.ValueLayout.Size);
        Assert.NotEmpty(emission.Body);
    }

    [Fact]
    public void SeparateEmissionsUseSeparateMethodContextsAndStableBytes()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var emitter = CreateEmitter(program);
        MethodEmissionContext? firstContext = null;
        MethodEmissionContext? secondContext = null;

        var first = EmitThroughContract(
            emitter,
            method,
            structured,
            roots,
            null,
            (code, _, context) =>
            {
                firstContext = context;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            });
        var second = EmitThroughContract(
            emitter,
            method,
            structured,
            roots,
            null,
            (code, _, context) =>
            {
                secondContext = context;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(0)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            });

        Assert.NotSame(firstContext, secondContext);
        Assert.Equal(first.Body, second.Body);
        Assert.Same(FilterEnvironmentLayout.Empty, first.FilterEnvironment);
        Assert.Same(FilterEnvironmentLayout.Empty, second.FilterEnvironment);
    }

    [Fact]
    public void UsesConstructedSignatureAndBoundaryWhenRootsArePresent()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(ConstructorKey);
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.Return));
        var roots = new MethodRootMap(
            ConstructorKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(
                new RootSource(RootSourceKind.Local, 0),
                0),
            []);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Constructed", isValueType: false),
            [],
            method.Signature);

        var result = EmitThroughContract(
            CreateEmitter(program),
            method,
            structured,
            roots,
            instance,
            static (code, _, _) => code.Write(
                WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        Assert.NotEmpty(result.Body);
        Assert.True(result.WasmInstructionCount > 0);
    }

    [Fact]
    public void ReservesManagedAddressForValueTypeReturn()
    {
        var program = new FakeProgram();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Result",
            isValueType: true);
        var method = program.GetMethod(ConstructorKey) with
        {
            Signature = MethodSignatureModel.Create(valueType),
        };
        var structured = Structure(
            program,
            method,
            I(0, CilOperation.DefaultValue, new CilOperand.TypeIdentity(valueType)),
            I(1, CilOperation.Return));

        var result = EmitThroughContract(
            CreateEmitter(program),
            method,
            structured,
            new MethodRootMap(ConstructorKey, [], []),
            null,
            static (code, _, _) => code.Write(
                WasmInstruction.NoOperand(WasmOpcodes.Unreachable)));

        Assert.NotEmpty(result.Body);
    }

    [Fact]
    public void ExceptionPayloadRootsShareTheMethodRootFrame()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var group = new StructuredExceptionGroup(new(0), null, 0, 1, [], [], [], null, null);
        var structured = Structure(program, method,
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.Return)) with
        {
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty.Add(group.Id, group),
        };
        var calls = 0;
        EmitThroughContract(CreateEmitter(program), method, structured,
            new MethodRootMap(EntryKey, [], []), null, (code, _, context) =>
            {
                calls++;
                Assert.Equal(0, context.RootMap.SlotCount);
                Assert.Equal(1, context.RootSlotCount);
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
            });
        Assert.Equal(1, calls);
    }

    private static IManagedMethodEmitter ThroughContract(IManagedMethodEmitter emitter) => emitter;

    private static ManagedMethodEmitter CreateEmitter(
        FakeProgram program,
        IValueFrameLayoutPlanner? valueFrames = null,
        ITerminalBranchValueFramePlanner? branchFrames = null,
        IMethodFrameEntryEmitter? frames = null)
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var types = CreateArgumentSignatureTypes(program);
        var frameEntry = frames ?? CreateMethodFrameEntry(program, layouts, imports);
        return new ManagedMethodEmitter(
            layouts,
            new ManagedMethodFunctionTypeResolver(),
            valueFrames ?? CreateValueFrameLayoutPlanner(program, layouts),
            new FilterEnvironmentLayoutPlanner(
                layouts,
                layouts,
                CreateArgumentTypes(program),
                types,
                new ExceptionGroupEnumerator()),
            new ExceptionGroupEnumerator(),
            frameEntry,
            new GeneratedFunctionWriterFactory(),
            new InstructionCountingWriterFactory(), branchFrames ?? new TerminalBranchValueFramePlanner(),
            new ArrayMetadataCachingPlanner(types),
            new ArrayMetadataSnapshotEmitter(layouts, layouts, CreateAddressInstructions(layouts),
                new ImplicitExceptionEmitter(layouts, layouts)),
            new ArrayStoreCheckPlanner(types, new UnusedTypeDefinitionResolver()),
            new EmptyFloatingCallPlanner(), new AuthoritativeRootSourceSelector(), new AuthoritativeRootLayoutPlanner(),
            new RootClearElisionPlanner(), new RootValueElisionPlanner(),
            new ValueFrameReferenceMapPlanner(layouts, layouts, types),
            new NetWasm.Compiler.GarbageCollection.RootSlotAllocator());
    }

    private sealed class EmptyFloatingCallPlanner : IScalarCallLifetimePlanner
    {
        public ScalarCallLifetimePlan Plan(StructuredMethod method, bool enabled) => ScalarCallLifetimePlan.Empty;
    }

    private sealed class RecordingBranchPlacement(TerminalBranchValueFramePlan plan) : ITerminalBranchValueFramePlanner
    {
        public int Calls { get; private set; }

        public TerminalBranchValueFramePlan? Plan(StructuredMethod method, ValueFrameLayout layout, MethodRootMap roots)
        {
            Calls++;
            return plan;
        }
    }

    private sealed class RecordingFrameEntry : IMethodFrameEntryEmitter
    {
        public MethodEmissionContext? Context { get; private set; }

        public void Emit(IWasmInstructionWriter code, StructuredMethodHeader header, MethodEmissionContext context) => Context = context;
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

    private delegate ManagedMethodEmission MethodEmitterCall(
        IManagedMethodEmitter emitter,
        MethodDefinitionModel method,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody);

    private static readonly MethodEmitterCall EmitThroughContract =
        static (emitter, method, structured, rootMap, methodInstance, emitBody) =>
            emitter.Emit(
                method,
                new ManagedMethodIdentity("Tests.Caller"),
                structured,
                rootMap,
                methodInstance,
                0,
                new RuntimeImportSelection(
                    WasmModuleProfile.CoreApplication,
                    IncludeTerminalExceptionReporter: true),
                emitBody);
}
