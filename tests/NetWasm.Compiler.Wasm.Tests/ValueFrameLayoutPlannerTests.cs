using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueFrameLayoutPlannerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FloatingCallSlotsAreAlignedDisjointAndDoNotPermanentlySpillLocals(WasmTarget target)
    {
        var fixture = new PlannerFixture(target);
        var body = new CilMethodBody(fixture.BodyMethod, 1,
            [CliValueKind.I4, CliValueKind.F4, CliValueKind.F8],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
             I(1, CilOperation.StoreLocal, new CilOperand.Index(1)),
             I(2, CilOperation.StoreLocal, new CilOperand.Index(2))]);
        var plan = new ScalarCallLifetimePlan(
            ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(10, [1, 2]).Add(20, [2]));

        var frame = ThroughContract(fixture.CreatePlanner()).Create(PlanningMethod(body),
            scalarCallLifetimes: plan);

        Assert.Same(plan, frame.ScalarCallLifetimes);
        Assert.Equal(2, frame.ScalarCallOffsets.Count);
        Assert.Equal(0, frame.ScalarCallOffsets[1] % 4);
        Assert.Equal(0, frame.ScalarCallOffsets[2] % 8);
        Assert.True(frame.ScalarCallOffsets[1] >= frame.LocalOffsets[0] + 4);
        Assert.True(frame.ScalarCallOffsets[2] >= frame.ScalarCallOffsets[1] + 4);
        Assert.True(frame.Size >= frame.ScalarCallOffsets[2] + 8);
        Assert.Equal([0], frame.SpilledScalarLocals.ToArray());
        Assert.Equal([0], frame.LocalOffsets.Keys.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void FloatingCallSlotsRejectNonFloatingOrInvalidLocals(int local)
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(fixture.BodyMethod, 0, [CliValueKind.I4], []);
        var plan = new ScalarCallLifetimePlan(
            ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(10, [local]));

        Assert.Throws<InvalidOperationException>(() => ThroughContract(fixture.CreatePlanner())
            .Create(PlanningMethod(body), scalarCallLifetimes: plan));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MutableByValueArgumentsOwnDistinctStorageWithoutCopyingReceiversOrByrefs(WasmTarget target)
    {
        var fixture = new PlannerFixture(target);
        var value = fixture.ValueType;
        var other = CliTypeIdentity.Named(Assembly, "Test", "OtherValue", true);
        var layouts = new MemberValueLayouts(new(value, 16, 8, [0]), new(other, 12, 4, []));
        var arguments = new IndexedArguments(
            [CliValueKind.ManagedAddress, CliValueKind.ValueType, CliValueKind.ValueType,
                CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.ManagedReference,
                CliValueKind.ValueType],
            [value, value, other, CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress),
                CliTypeIdentity.FromStackKind(CliValueKind.I4), fixture.ReferenceType, value]);
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
        [
            I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
            I(2, CilOperation.StoreArgument, new CilOperand.Index(2)),
            I(3, CilOperation.LoadArgumentAddress, new CilOperand.Index(3)),
            I(4, CilOperation.LoadArgumentAddress, new CilOperand.Index(4)),
            I(5, CilOperation.LoadArgumentAddress, new CilOperand.Index(5)),
            I(6, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
            I(7, CilOperation.LoadArgument, new CilOperand.Index(6)),
        ]);
        var planner = new ValueFrameLayoutPlanner(fixture, fixture, fixture.Layouts, layouts,
            fixture.TypeOperands, fixture.TypeIdentities, arguments, arguments,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);

        var frame = ThroughContract(planner).Create(PlanningMethod(body, (2, 1)));

        Assert.Equal([1, 2, 4, 5], frame.ArgumentOffsets.Keys.Order().ToArray());
        Assert.Equal(0, frame.ArgumentOffsets[1] % 8);
        Assert.Equal(0, frame.ArgumentOffsets[2] % 4);
        Assert.True(frame.ArgumentOffsets[2] >= frame.ArgumentOffsets[1] + 16);
        Assert.True(frame.TemporaryOffsets[7] >= frame.ArgumentOffsets[2] + 12);
        Assert.Equal(2, layouts.Requests.Count(type => type.Equals(value)));
        Assert.Equal(1, layouts.Requests.Count(type => type.Equals(other)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void AddressTakenValueArgumentsOwnStorageOnlyForProvedEffects(
        int effectKind,
        bool expectedOwned)
    {
        var fixture = new PlannerFixture();
        var value = fixture.ValueType;
        var arguments = new IndexedArguments(
            [CliValueKind.ValueType],
            [value]);
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
        [
            I(0, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
        ]);
        var planner = new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            new MemberValueLayouts(new ValueLayout(value, 16, 8, [0])),
            fixture.TypeOperands,
            fixture.TypeIdentities,
            arguments,
            arguments,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);
        var effect = effectKind switch
        {
            1 => new ManagedByRefMethodEffects([0], [], []),
            2 => new ManagedByRefMethodEffects([], [0], []),
            3 => new ManagedByRefMethodEffects([], [], [0]),
            _ => ManagedByRefMethodEffects.Empty,
        };

        var frame = ThroughContract(planner).Create(
            PlanningMethod(body),
            byRefEffects: effect);

        Assert.Equal(expectedOwned, frame.ArgumentOffsets.ContainsKey(0));
    }

    [Fact]
    public void StoredValueArgumentsRemainOwnedDespiteAnEmptyEffectSummary()
    {
        var fixture = new PlannerFixture();
        var value = fixture.ValueType;
        var arguments = new IndexedArguments(
            [CliValueKind.ValueType],
            [value]);
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
        [
            I(0, CilOperation.StoreArgument, new CilOperand.Index(0)),
        ]);
        var planner = new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            new MemberValueLayouts(new ValueLayout(value, 16, 8, [0])),
            fixture.TypeOperands,
            fixture.TypeIdentities,
            arguments,
            arguments,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);

        var frame = ThroughContract(planner).Create(
            PlanningMethod(body, (0, 1)),
            byRefEffects: ManagedByRefMethodEffects.Empty);

        Assert.Contains(0, frame.ArgumentOffsets.Keys);
    }

    [Fact]
    public void PromotedCallAndLocalDoNotReserveValueFrameStorage()
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [CliValueKind.ValueType],
            [
                I(0, CilOperation.Call,
                    new CilOperand.MethodInstance(fixture.ValueStaticInstance)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(3, CilOperation.Pop),
            ])
        {
            LocalSignatureTypes = [fixture.ValueType],
        };
        var promotions = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(
                0,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            [],
            [1, 2],
            [0]);

        var layout = fixture.CreatePlanner(
            new FixedTransientAggregatePromotionPlanner(promotions)).Create(
                PlanningMethod(body, (1, 1), (3, 1)));

        Assert.Equal(0, layout.Size);
        Assert.Empty(layout.LocalOffsets);
        Assert.Empty(layout.TemporaryOffsets);
        Assert.Same(promotions, layout.TransientAggregatePromotions);
    }

    private sealed class IndexedArguments(CliValueKind[] kinds, CliTypeIdentity[] types) :
        IArgumentTypeResolver, IArgumentSignatureTypeResolver
    {
        CliValueKind IArgumentTypeResolver.Resolve(StructuredMethodHeader header, int index) => kinds[index];
        CliTypeIdentity IArgumentSignatureTypeResolver.Resolve(StructuredMethodHeader header, int index) => types[index];
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MemberCallsReuseAlignedScratchForTheirLargestResult(WasmTarget target)
    {
        var fixture = new PlannerFixture();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var small = NullableType(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var large = NullableType(CliTypeIdentity.Primitive("i8", CliValueKind.I8));
        var values = new MemberValueLayouts(
            new ValueLayout(small, 8, 4, []), new ValueLayout(large, 16, 8, []));
        var invoker = MemberMethod(0x06000070, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        fixture.AddMethod(invoker.Definition);
        var plan = MemberPlan(invoker, large, small, small);
        var planner = new ValueFrameLayoutPlanner(
            fixture, fixture, layouts, values, fixture.TypeOperands, fixture.TypeIdentities,
            fixture.ArgumentTypes, fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
        [
            I(0, CilOperation.LocalAllocate),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(invoker)),
            I(7, CilOperation.Call, new CilOperand.Entity(invoker.Definition.Key)),
            I(11, CilOperation.Call, new CilOperand.MethodInstance(fixture.ReferenceInstance)),
        ]);

        var frame = ThroughContract(planner).Create(PlanningMethod(body), memberExecution: plan);

        Assert.Equal(2, frame.MemberResultOffsets.Count);
        Assert.Equal(8, frame.MemberResultOffsets[3]);
        Assert.Equal(8, frame.MemberResultOffsets[7]);
        Assert.Equal(24, frame.Size);
        Assert.Equal(2, values.Requests.Count(type => type.Equals(small)));
        Assert.Equal(2, values.Requests.Count(type => type.Equals(large)));
        Assert.Empty(frame.TemporaryOffsets);
        Assert.Empty(frame.NativeArgumentOffsets);
        Assert.Empty(frame.LocalOffsets);
        Assert.Empty(frame.ArgumentOffsets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MemberScratchRequiresBothAnInvokerCallAndAnAggregateResult(int scenario)
    {
        var fixture = new PlannerFixture();
        var nullable = NullableType(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var values = new MemberValueLayouts(new ValueLayout(nullable, 8, 4, []));
        var invoker = MemberMethod(0x06000070, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var populated = MemberPlan(invoker, nullable);
        MemberExecutionPlan? plan = scenario switch
        {
            0 => null,
            1 => MemberExecutionPlan.Empty,
            2 => populated with { MethodInvokers = [] },
            3 => populated with { Methods = ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty },
            4 => MemberPlan(invoker, CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
            _ => populated,
        };
        var body = new CilMethodBody(fixture.BodyMethod, 1, [], scenario == 5
            ? []
            : [I(3, CilOperation.Call, new CilOperand.MethodInstance(invoker))]);
        var planner = new ValueFrameLayoutPlanner(
            fixture, fixture, fixture.Layouts, values, fixture.TypeOperands, fixture.TypeIdentities,
            fixture.ArgumentTypes, fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);

        var frame = ThroughContract(planner).Create(PlanningMethod(body), memberExecution: plan);

        Assert.Equal(0, frame.Size);
        Assert.Empty(frame.MemberResultOffsets);
        Assert.Empty(frame.TemporaryOffsets);
        Assert.Empty(values.Requests);
    }

    [Fact]
    public void MemberScratchRejectsResultsThatWouldRequireManagedRoots()
    {
        var fixture = new PlannerFixture();
        var nullable = NullableType(CliTypeIdentity.Named(Assembly, "Test", "ReferenceValue", true));
        var values = new MemberValueLayouts(new ValueLayout(nullable, 16, 8, [8]));
        var invoker = MemberMethod(0x06000070, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
            [I(19, CilOperation.Call, new CilOperand.MethodInstance(invoker))]);
        var planner = new ValueFrameLayoutPlanner(
            fixture, fixture, fixture.Layouts, values, fixture.TypeOperands, fixture.TypeIdentities,
            fixture.ArgumentTypes, fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance);

        var error = Assert.Throws<CompilerException>(() => ThroughContract(planner).Create(
            PlanningMethod(body), memberExecution: MemberPlan(invoker, nullable)));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Contains("managed references", error.Diagnostic.Message);
        Assert.Equal(invoker.Definition.Name, error.Diagnostic.Method);
        Assert.Equal(19, error.Diagnostic.IlOffset);
        Assert.Equal([nullable], values.Requests);
    }

    private static CliTypeIdentity NullableType(CliTypeIdentity underlying) =>
        CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [underlying]);

    private static MethodInstanceModel MemberMethod(int token, CliTypeIdentity result)
    {
        var signature = MethodSignatureModel.Create(result);
        var definition = new MethodDefinitionModel(Key(token), TypeKey, $"Member{token}", true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "Test", "Members", false), [], signature);
    }

    private static MemberExecutionPlan MemberPlan(MethodInstanceModel invoker, params CliTypeIdentity[] results) =>
        new(results.Select((type, index) => MemberMethod(0x06000080 + index, type))
                .ToImmutableDictionary(method => method.CanonicalName,
                    method => new MemberMethodExecutionPlan(method, false, [])),
            ImmutableDictionary<string, FieldInstanceModel>.Empty, null)
        {
            MethodInvokers = [invoker.Definition.Key],
        };

    private sealed class MemberValueLayouts(params ValueLayout[] layouts) : IValueLayoutProvider
    {
        public List<CliTypeIdentity> Requests { get; } = [];

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            Requests.Add(type);
            return layouts.Single(layout => layout.Type.Equals(type));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NativeCallsReusePerCallCopiesAndStackResultStorage(WasmTarget target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "NativePair", true);
        var pointer = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var logical = MethodSignatureModel.Create(valueType, valueType, valueType);
        var method = NativeMethod(logical);
        var parameter = new NativeAbiValuePlan(NativeAbiValueKind.IndirectAggregate, valueType, pointer, 12, 4);
        var result = parameter with { Size = 16, Alignment = 16 };
        var abi = new NativeAbiPlan(method.Definition.NativeImport!, new(logical,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt,
                CliValueKind.NativeInt, CliValueKind.NativeInt),
            [new(0, 1, parameter), new(1, 2, parameter)], result, 0));
        var imports = new NativeImportPlan([new(method, abi,
            new(RuntimeAbi.RuntimeModule, "aggregate", new(abi.Signature.ParameterTypes, abi.Signature.ReturnType)))]);
        var body = new CilMethodBody(program.GetMethod(EntryKey), 2, [],
        [
            I(0, CilOperation.LocalAllocate),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(method)),
            I(7, CilOperation.Call, new CilOperand.MethodInstance(method)),
        ]);

        var frame = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body, (3, 2), (7, 2)), imports);

        Assert.Equal(32, frame.NativeArgumentOffsets[(3, 0)]);
        Assert.Equal(44, frame.NativeArgumentOffsets[(3, 1)]);
        Assert.Equal(16, frame.TemporaryOffsets[3]);
        Assert.Equal(32, frame.NativeArgumentOffsets[(7, 0)]);
        Assert.Equal(44, frame.NativeArgumentOffsets[(7, 1)]);
        Assert.Equal(16, frame.TemporaryOffsets[7]);
        Assert.Equal(56, frame.Size);
        Assert.Empty(frame.LocalOffsets);
        Assert.Empty(frame.ArgumentOffsets);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, NativeAbiValueKind.ScalarizedAggregate, 2, 2)]
    [InlineData(WasmTarget.Wasm64, NativeAbiValueKind.ScalarizedAggregate, 2, 2)]
    [InlineData(WasmTarget.Wasm32, NativeAbiValueKind.IgnoredAggregate, 1, 1)]
    [InlineData(WasmTarget.Wasm64, NativeAbiValueKind.IgnoredAggregate, 1, 1)]
    public void DirectAndIgnoredAggregateResultsStillHaveManagedStorage(
        WasmTarget target, NativeAbiValueKind kind, int size, int alignment)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "NativeValue", true);
        var scalar = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
        var logical = MethodSignatureModel.Create(valueType, valueType);
        var method = NativeMethod(logical);
        var value = new NativeAbiValuePlan(kind, valueType,
            kind == NativeAbiValueKind.IgnoredAggregate ? null : scalar, size, alignment, scalar);
        var physical = kind == NativeAbiValueKind.IgnoredAggregate
            ? MethodSignatureModel.Create(CliValueKind.Void) : MethodSignatureModel.Create(scalar, scalar);
        var abi = new NativeAbiPlan(method.Definition.NativeImport!,
            new(logical, physical, [new(0, kind == NativeAbiValueKind.IgnoredAggregate ? null : 0, value)], value, null));
        var imports = new NativeImportPlan([new(method, abi,
            new(RuntimeAbi.RuntimeModule, "aggregate", new(physical.ParameterTypes, physical.ReturnType)))]);
        var body = new CilMethodBody(program.GetMethod(EntryKey), 1, [],
            [I(3, CilOperation.Call, new CilOperand.MethodInstance(method))]);

        var frame = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body, (3, 1)), imports);

        Assert.Equal(0, Assert.Single(frame.TemporaryOffsets).Value);
        Assert.Equal(layouts.Target.ObjectReferenceAlignment, frame.Size);
        Assert.Empty(frame.NativeArgumentOffsets);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    [InlineData(32, 32)]
    public void InvalidNativeStorageFailsWithoutPublishingAFrame(int size, int alignment)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "NativeValue", true);
        var logical = MethodSignatureModel.Create(valueType);
        var method = NativeMethod(logical);
        var physical = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.NativeInt);
        var abi = new NativeAbiPlan(method.Definition.NativeImport!, new(logical, physical, [],
            new(NativeAbiValueKind.IndirectAggregate, valueType,
                CliTypeIdentity.FromStackKind(CliValueKind.NativeInt), size, alignment), 0));
        var imports = new NativeImportPlan([new(method, abi,
            new(RuntimeAbi.RuntimeModule, "aggregate", new(physical.ParameterTypes, physical.ReturnType)))]);
        var body = new CilMethodBody(program.GetMethod(EntryKey), 0, [],
            [I(3, CilOperation.Call, new CilOperand.MethodInstance(method))]);

        var error = Assert.Throws<CompilerException>(() =>
            ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
                PlanningMethod(body), imports));

        Assert.Equal(DiagnosticCode.CompilerInvariant, error.Diagnostic.Code);
    }

    [Fact]
    public void NativeCallWithoutPublishedPlanRejects()
    {
        var program = new FakeProgram();
        var method = NativeMethod(MethodSignatureModel.Create(CliValueKind.Void));
        var body = new CilMethodBody(program.GetMethod(EntryKey), 0, [],
            [I(3, CilOperation.Call, new CilOperand.MethodInstance(method))]);
        var planner = ThroughContract(CreateValueFrameLayoutPlanner(program, new RecordingLayoutProvider()));

        Assert.Equal(DiagnosticCode.CompilerInvariant,
            Assert.Throws<CompilerException>(() => planner.Create(PlanningMethod(body))).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.CompilerInvariant,
            Assert.Throws<CompilerException>(() => planner.Create(
                PlanningMethod(body), NativeImportPlan.Empty)).Diagnostic.Code);
    }

    private static MethodInstanceModel NativeMethod(MethodSignatureModel signature)
    {
        var definition = new MethodDefinitionModel(Key(0x0600007f), TypeKey, "Native", true, signature, 0)
        {
            NativeImport = new("mule", "aggregate", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        return new(definition, CliTypeIdentity.Named(Assembly, "Test", "Native", false), [], signature);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm64, 0)]
    [InlineData(WasmTarget.Wasm32, 1)]
    [InlineData(WasmTarget.Wasm64, 1)]
    [InlineData(WasmTarget.Wasm32, 2)]
    [InlineData(WasmTarget.Wasm64, 2)]
    [InlineData(WasmTarget.Wasm32, 3)]
    [InlineData(WasmTarget.Wasm64, 3)]
    public void ArrayValueCopiesReuseStorageAtTheSameStackDepth(
        WasmTarget target,
        int rank)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(Assembly, "Test", "ReferencePair", isValueType: true);
        var values = new ArrayElementValueLayouts(valueType, layouts.Target.ObjectReferenceSize);
        var planner = ThroughContract(new ValueFrameLayoutPlanner(
            program, program, layouts, values,
            CreateTypeOperands(program), CreateTypeIdentities(program),
            CreateArgumentTypes(program), CreateArgumentSignatureTypes(program),
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance));
        var operation = rank == 0
            ? CilOperation.LoadArrayElement
            : CilOperation.LoadRectangularArrayElement;
        var body = new CilMethodBody(program.GetMethod(EntryKey), 4, [],
        [
            I(0, CilOperation.LocalAllocate),
            I(3, operation, Operand(valueType)),
            I(7, operation, Operand(valueType)),
            I(11, operation, Operand(CliTypeIdentity.FromStackKind(CliValueKind.I4))),
            I(15, operation, Operand(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference))),
        ]);

        var entryCount = rank == 0 ? 2 : rank + 1;
        var layout = planner.Create(PlanningMethod(
            body,
            (3, entryCount),
            (7, entryCount)));

        var alignment = layouts.Target.ObjectReferenceAlignment;
        Assert.Equal(2, layout.TemporaryOffsets.Count);
        Assert.Equal(alignment, layout.TemporaryOffsets[3]);
        Assert.Equal(alignment, layout.TemporaryOffsets[7]);
        Assert.Equal(alignment + values.Layout.Size, layout.Size);
        Assert.Equal([valueType, valueType], values.Requests);
        Assert.Empty(layout.LocalOffsets);
        Assert.Empty(layout.ArgumentOffsets);

        CilOperand.TypeIdentity Operand(CliTypeIdentity element) => new(
            rank == 0 ? element : CliTypeIdentity.Array(element, rank));
    }

    [Fact]
    public void SimultaneouslyLiveStackDepthsKeepDistinctStorage()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var valueType = CliTypeIdentity.FromStackKind(CliValueKind.ValueType);
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            2,
            [],
            [
                I(3, CilOperation.DefaultValue, new CilOperand.TypeIdentity(valueType)),
                I(7, CilOperation.DefaultValue, new CilOperand.TypeIdentity(valueType)),
            ]);

        var frame = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body, (3, 0), (7, 1)));

        Assert.NotEqual(frame.TemporaryOffsets[3], frame.TemporaryOffsets[7]);
        Assert.Equal(2, frame.TemporaryOffsets.Values.Distinct().Count());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedAggregateResultsDoNotAliasConsumedValues(WasmTarget target)
    {
        var fixture = new PlannerFixture(target);
        var transformSignature = MethodSignatureModel.Create(
            fixture.ValueType,
            fixture.ValueType);
        var transformDefinition = new MethodDefinitionModel(
            Key(0x06000070),
            fixture.ValueStatic.DeclaringType,
            "Transform",
            true,
            transformSignature,
            1);
        var transform = new MethodInstanceModel(
            transformDefinition,
            fixture.ReferenceType,
            [],
            transformSignature);
        var constructorSignature = MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            fixture.ValueType);
        var constructorDefinition = new MethodDefinitionModel(
            Key(0x06000071),
            fixture.ValueConstructor.DeclaringType,
            ".ctor",
            false,
            constructorSignature,
            1);
        var constructor = new MethodInstanceModel(
            constructorDefinition,
            fixture.ValueType,
            [],
            constructorSignature);
        var cases = new[]
        {
            new CilMethodBody(
                fixture.BodyMethod,
                1,
                [],
                [
                    I(3, CilOperation.DefaultValue,
                        new CilOperand.TypeIdentity(fixture.ValueType)),
                    I(7, CilOperation.Call,
                        new CilOperand.MethodInstance(fixture.ValueInstance)),
                ]),
            new CilMethodBody(
                fixture.BodyMethod,
                1,
                [],
                [
                    I(3, CilOperation.DefaultValue,
                        new CilOperand.TypeIdentity(fixture.ValueType)),
                    I(7, CilOperation.Call,
                        new CilOperand.MethodInstance(transform)),
                ]),
            new CilMethodBody(
                fixture.BodyMethod,
                1,
                [],
                [
                    I(3, CilOperation.DefaultValue,
                        new CilOperand.TypeIdentity(fixture.ValueType)),
                    I(7, CilOperation.NewObject,
                        new CilOperand.MethodInstance(constructor)),
                ]),
        };
        var valueSize = fixture.Layouts.GetValueLayout(fixture.ValueType).Size;

        foreach (var body in cases)
        {
            var frame = ThroughContract(fixture.CreatePlanner()).Create(
                PlanningMethod(body, (3, 0), (7, 1)));
            var input = frame.TemporaryOffsets[3];
            var result = frame.TemporaryOffsets[7];

            Assert.True(
                input + valueSize <= result || result + valueSize <= input,
                $"input [{input}, {input + valueSize}) overlaps result " +
                $"[{result}, {result + valueSize})");
        }
    }

    [Fact]
    public void ScalarReceiverScratchDoesNotOverlapAggregateCallResult()
    {
        var fixture = new PlannerFixture();
        var scalarReceiver = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var method = fixture.ValueInstance with
        {
            DeclaringType = scalarReceiver,
        };
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [I(3, CilOperation.Call, new CilOperand.MethodInstance(method))]);

        var frame = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body, (3, 1)));

        Assert.NotEqual(
            frame.TemporaryOffsets[3],
            frame.CallReceiverOffsets[3]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BoundedArrayConstructionReservesLengthsAndLowerBounds(WasmTarget target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var array = CliTypeIdentity.Array(CliTypeIdentity.FromStackKind(CliValueKind.I4), 3);
        var body = new CilMethodBody(program.GetMethod(EntryKey), 6, [],
            [I(17, CilOperation.NewBoundedRectangularArray, new CilOperand.TypeIdentity(array))]);

        var layout = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body));

        Assert.Equal(24, layout.Size);
        Assert.Equal(0, Assert.Single(layout.TemporaryOffsets).Value);
        Assert.True(layout.TemporaryOffsets.ContainsKey(17));
    }

    [Fact]
    public void PlansAddressTakenArgumentsLocalsAndValueTemporaries()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [CliValueKind.I4, CliValueKind.ValueType, CliValueKind.I4],
            [
                I(0, CilOperation.LocalAllocate),
                I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                I(3, CilOperation.DefaultValue, new CilOperand.TypeIdentity(
                    CliTypeIdentity.FromStackKind(CliValueKind.ValueType))),
            ]);

        Func<IValueFrameLayoutPlanner, ValueFrameLayout> contract = planner =>
            planner.Create(PlanningMethod(body));
        var layout = contract(CreateValueFrameLayoutPlanner(program, layouts));

        Assert.Equal(16, layout.Size);
        Assert.Equal(4, layout.ArgumentOffsets[0]);
        Assert.Equal(8, layout.LocalOffsets[0]);
        Assert.DoesNotContain(1, layout.LocalOffsets.Keys);
        Assert.Equal(12, layout.TemporaryOffsets[3]);
        Assert.Equal([0], layout.SpilledScalarLocals);
    }

    [Fact]
    public void PromotedAddressTakenScalarKeepsFrameShadowWithoutPermanentSpill()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [CliValueKind.I4],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))])
        {
            LocalSignatureTypes = [type],
        };
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type),
            []);

        var layout = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body),
            addressTakenScalarPromotions: plan);

        Assert.Equal(0, layout.LocalOffsets[0]);
        Assert.Empty(layout.SpilledScalarLocals);
        Assert.Same(plan, layout.AddressTakenScalarPromotions);
    }

    [Fact]
    public void PromotedAddressTakenAggregateKeepsItsFrameShadow()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var type = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Buffer",
            isValueType: true);
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [CliValueKind.ValueType],
            [I(0, CilOperation.LoadLocalAddress, new CilOperand.Index(0))])
        {
            LocalSignatureTypes = [type],
        };
        var plan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                0,
                AddressTakenAggregateLocalPlan.CreateI4Storage(type, 12)),
            [],
            []);

        var layout = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(
            PlanningMethod(body),
            addressTakenAggregatePromotions: plan);

        Assert.Equal(0, layout.LocalOffsets[0]);
        Assert.Same(plan, layout.AddressTakenAggregatePromotions);
    }

    [Fact]
    public void EmptyMethodNeedsNoValueFrameStorage()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var planner = ThroughContract(CreateValueFrameLayoutPlanner(
            program,
            layouts));
        var body = new CilMethodBody(program.GetMethod(EntryKey), 0, [], []);

        var layout = planner.Create(PlanningMethod(body));

        Assert.Equal(0, layout.Size);
        Assert.Empty(layout.ArgumentOffsets);
        Assert.Empty(layout.LocalOffsets);
        Assert.Empty(layout.TemporaryOffsets);
        Assert.Empty(layout.SpilledScalarLocals);
    }

    [Fact]
    public void PlansEveryValueTemporaryOperandThroughItsContract()
    {
        var fixture = new PlannerFixture();
        var valueType = fixture.ValueType;
        var referenceType = fixture.ReferenceType;
        var rectangularArray = CliTypeIdentity.Array(referenceType, 3);
        var valueField = new FieldInstanceModel(
            fixture.ValueField,
            referenceType,
            valueType);
        var scalarField = new FieldInstanceModel(
            fixture.ScalarField,
            referenceType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var body = new CilMethodBody(
            fixture.BodyMethod,
            4,
            [CliValueKind.I4, CliValueKind.ValueType],
            [
                I(0, CilOperation.LocalAllocate),
                I(1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                I(2, CilOperation.LoadArgumentAddress, new CilOperand.Index(1)),
                I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                I(4, CilOperation.LoadArgument, new CilOperand.Index(1)),
                I(5, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(6, CilOperation.LoadLocal, new CilOperand.Index(1)),
                I(7, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(8, CilOperation.DefaultValue, new CilOperand.TypeIdentity(valueType)),
                I(9, CilOperation.DefaultValue, new CilOperand.TypeIdentity(referenceType)),
                I(10, CilOperation.LoadArrayElement,
                    new CilOperand.TypeIdentity(valueType)),
                I(11, CilOperation.LoadArrayElement,
                    new CilOperand.TypeIdentity(referenceType)),
                I(12, CilOperation.LoadObject, new CilOperand.TypeIdentity(valueType)),
                I(13, CilOperation.LoadObject, new CilOperand.TypeIdentity(referenceType)),
                I(14, CilOperation.LoadField, new CilOperand.FieldInstance(valueField)),
                I(15, CilOperation.LoadField, new CilOperand.Entity(fixture.ScalarField.Key)),
                I(16, CilOperation.LoadStaticField,
                    new CilOperand.Entity(fixture.ValueField.Key)),
                I(17, CilOperation.LoadStaticField,
                    new CilOperand.FieldInstance(scalarField)),
                I(18, CilOperation.Call,
                    new CilOperand.MethodInstance(fixture.ValueStaticInstance)),
                I(19, CilOperation.Call, new CilOperand.Entity(fixture.ScalarStatic.Key)),
                I(20, CilOperation.CallVirtual,
                    new CilOperand.MethodInstance(fixture.ScalarReceiverInstance)),
                I(21, CilOperation.CallVirtual,
                    new CilOperand.Entity(fixture.ValueInstance.Definition.Key)),
                I(22, CilOperation.Call,
                    new CilOperand.MethodInstance(fixture.ReferenceInstance)),
                I(23, CilOperation.UnboxAny, new CilOperand.TypeIdentity(valueType)),
                I(24, CilOperation.UnboxAny, new CilOperand.TypeIdentity(referenceType)),
                I(25, CilOperation.NewObject,
                    new CilOperand.MethodInstance(fixture.ValueConstructorInstance)),
                I(26, CilOperation.NewObject,
                    new CilOperand.Entity(fixture.ValueConstructor.Key)),
                I(27, CilOperation.NewObject,
                    new CilOperand.Entity(fixture.ReferenceConstructor.Key)),
                    I(28, CilOperation.NewRectangularArray, new CilOperand.TypeIdentity(rectangularArray))]);

        var layout = ThroughContract(fixture.CreatePlanner()).Create(PlanningMethod(
            body,
            (10, 2),
            (12, 1),
            (14, 1),
            (20, 1),
            (21, 1),
            (23, 1)));

        Assert.Contains(0, layout.ArgumentOffsets.Keys);
        Assert.Contains(0, layout.LocalOffsets.Keys);
        Assert.Contains(1, layout.LocalOffsets.Keys);
        Assert.Contains(0, layout.SpilledScalarLocals);
        foreach (var offset in new[]
        {
            4, 6, 8, 10, 12, 14, 16, 18, 21, 23, 25, 26, 28,
        })
        {
            Assert.Contains(offset, layout.TemporaryOffsets.Keys);
        }
        Assert.DoesNotContain(5, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(7, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(9, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(11, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(13, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(15, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(17, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(19, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(20, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(22, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(24, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(27, layout.TemporaryOffsets.Keys);
        Assert.Equal(20, Assert.Single(layout.CallReceiverOffsets).Key);
        Assert.True(layout.TemporaryOffsets.Values.Distinct().Count() <
            layout.TemporaryOffsets.Count);
    }

    [Fact]
    public void RejectsMalformedFieldCallAndConstructorOperands()
    {
        var fixture = new PlannerFixture();
        AssertPlannerFailure(
            fixture,
            CilOperation.LoadField,
            "field load has no field operand");
        AssertPlannerFailure(
            fixture,
            CilOperation.Call,
            "call has no method operand");
        AssertPlannerFailure(
            fixture,
            CilOperation.NewObject,
            "constructor has no method operand");
        AssertPlannerFailure(
            fixture,
            CilOperation.CreateReadOnlySpanFromRva,
            "RVA span creation has no span data operand");
    }

    [Fact]
    public void RvaSpanCreationReservesItsValueResult()
    {
        var fixture = new PlannerFixture();
        var element = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var source = new FieldInstanceModel(
            fixture.ValueField,
            fixture.ValueType,
            element);
        var operand = new CilOperand.ReadOnlySpanData(
            source,
            fixture.ValueType,
            element,
            1);
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [I(0, CilOperation.CreateReadOnlySpanFromRva, operand)]);

        var layout = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body));

        Assert.Contains(0, layout.TemporaryOffsets.Keys);
    }

    [Fact]
    public void DirectValueLoadsImmediatelyConsumedAreSelectedThroughContract()
    {
        var fixture = new PlannerFixture();
        var selector = ThroughSelector(new DirectValueLoadSelector());

        foreach (var operation in new[]
                 {
                     CilOperation.LoadArgument,
                     CilOperation.LoadLocal,
                     CilOperation.LoadArrayElement,
                     CilOperation.LoadRectangularArrayElement,
                     CilOperation.LoadObject,
                 })
            foreach (var consumer in new[]
                     {
                     CilOperation.Return,
                     CilOperation.StoreLocal,
                     CilOperation.StoreArgument,
                 })
            {
                var body = new CilMethodBody(
                    fixture.BodyMethod,
                    1,
                    [],
                    [
                        I(0, operation, operation switch
                    {
                        CilOperation.LoadArrayElement or CilOperation.LoadObject =>
                            new CilOperand.TypeIdentity(fixture.ValueType),
                        CilOperation.LoadRectangularArrayElement =>
                            new CilOperand.TypeIdentity(CliTypeIdentity.Array(fixture.ValueType, 2)),
                        _ => new CilOperand.Index(0),
                    }),
                    consumer == CilOperation.Return
                        ? I(1, consumer)
                        : I(1, consumer, new CilOperand.Index(0)),
                    ]);

                Assert.Equal([0], selector.Select(
                    PlanningMethod(body, (0, 0), (1, 1))));
            }

        var unreachableBody = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Return),
            ]);
        var unreachable = PlanningMethod(unreachableBody) with
        {
            InstructionEntryStacks =
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
        };
        Assert.Empty(selector.Select(unreachable));

        Assert.Throws<ArgumentNullException>(() => selector.Select(null!));
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal, CilOperation.StoreLocal)]
    [InlineData(CilOperation.LoadArgument, CilOperation.StoreLocal)]
    [InlineData(CilOperation.LoadLocal, CilOperation.StoreArgument)]
    [InlineData(CilOperation.LoadArgument, CilOperation.StoreArgument)]
    public void SuspendedValueLoadTransfersAfterIndependentArrayArgument(
        CilOperation load, CilOperation store)
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(fixture.BodyMethod, 3, [],
        [
            I(0, load, new CilOperand.Index(0)),
            I(1, CilOperation.LoadNull),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(3, CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(fixture.ValueType)),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(5, store, new CilOperand.Index(2)),
        ]);

        var selected = ThroughSelector(new DirectValueLoadSelector()).Select(
            PlanningMethod(body, (0, 0), (1, 1), (2, 2), (3, 3), (4, 2), (5, 1)));

        Assert.Equal([0, 3], selected.Order());
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal, CilOperation.StoreLocal)]
    [InlineData(CilOperation.LoadArgument, CilOperation.StoreArgument)]
    public void SuspendedValueLoadKeepsSnapshotWhenItsSourceIsReassigned(
        CilOperation load, CilOperation store)
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(fixture.BodyMethod, 2, [],
        [
            I(0, load, new CilOperand.Index(0)),
            I(1, load, new CilOperand.Index(1)),
            I(2, store, new CilOperand.Index(0)),
            I(3, store, new CilOperand.Index(2)),
        ]);

        var selected = ThroughSelector(new DirectValueLoadSelector()).Select(
            PlanningMethod(body, (0, 0), (1, 1), (2, 2), (3, 1)));

        Assert.Equal([1], selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuspendedValueLoadKeepsSnapshotAcrossCallsAndAddressExposure(bool address)
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(fixture.BodyMethod, 2, [],
        [
            I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            address
                ? I(1, CilOperation.LoadLocalAddress, new CilOperand.Index(0))
                : I(1, CilOperation.Call, new CilOperand.MethodInstance(fixture.ValueStaticInstance)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(3, CilOperation.StoreLocal, new CilOperand.Index(2)),
        ]);

        var selected = ThroughSelector(new DirectValueLoadSelector()).Select(
            PlanningMethod(body, (0, 0), (1, 1), (2, 2), (3, 1)));

        Assert.Empty(selected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DirectValueLoadSelectionRejectsOtherInstructionShapes(int scenario)
    {
        var fixture = new PlannerFixture();
        var instructions = scenario switch
        {
            0 => new[]
            {
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            },
            1 =>
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Nop),
            ],
            2 =>
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(2, CilOperation.Return),
            ],
            _ =>
            [
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.Return),
            ],
        };
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [.. instructions]);
        var selector = ThroughSelector(new DirectValueLoadSelector());

        var selected = selector.Select(PlanningMethod(body));

        Assert.Empty(selected);
    }

    [Fact]
    public void DirectArgumentReturnDoesNotReserveAValueTemporary()
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(1)),
                I(1, CilOperation.Return),
            ]);

        var layout = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body, (0, 0), (1, 1)));

        Assert.Equal([0], layout.DirectValueLoadOffsets);
        Assert.Empty(layout.TemporaryOffsets);
        Assert.Equal(0, layout.Size);

        var scalarBody = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Return),
            ]);
        var scalarLayout = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(scalarBody, (0, 0), (1, 1)));
        Assert.Empty(scalarLayout.DirectValueLoadOffsets);
    }

    [Fact]
    public void DirectLocalReturnKeepsOnlyPersistentLocalStorage()
    {
        var fixture = new PlannerFixture();
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [CliValueKind.ValueType],
            [
                I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(1, CilOperation.Return),
            ])
        {
            LocalSignatureTypes = [fixture.ValueType],
        };

        var layout = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body, (0, 0), (1, 1)));

        Assert.Equal([0], layout.DirectValueLoadOffsets);
        Assert.Empty(layout.TemporaryOffsets);
        Assert.Equal([0], layout.LocalOffsets.Keys);
        Assert.True(layout.Size >= fixture.Layouts.GetValueLayout(
            fixture.ValueType).Size);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ImmediateArrayAssignmentKeepsDestinationButOmitsSnapshot(
        WasmTarget target, bool rectangular)
    {
        var fixture = new PlannerFixture(target);
        var body = new CilMethodBody(
            fixture.BodyMethod,
            3,
            [CliValueKind.ValueType],
            [
                I(0, rectangular
                        ? CilOperation.LoadRectangularArrayElement
                        : CilOperation.LoadArrayElement,
                    new CilOperand.TypeIdentity(rectangular
                        ? CliTypeIdentity.Array(fixture.ValueType, 2)
                        : fixture.ValueType)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            ])
        {
            LocalSignatureTypes = [fixture.ValueType],
        };

        var layout = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body, (0, rectangular ? 3 : 2), (1, 1)));

        Assert.Equal([0], layout.DirectValueLoadOffsets);
        Assert.Empty(layout.TemporaryOffsets);
        Assert.Equal([0], layout.LocalOffsets.Keys);
        Assert.True(layout.Size >= fixture.Layouts.GetValueLayout(fixture.ValueType).Size);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadLocal, CilOperation.StoreLocal)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadLocal, CilOperation.StoreLocal)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadArgument, CilOperation.StoreLocal)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadArgument, CilOperation.StoreLocal)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadLocal, CilOperation.StoreArgument)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadLocal, CilOperation.StoreArgument)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadArgument, CilOperation.StoreArgument)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadArgument, CilOperation.StoreArgument)]
    public void ImmediateAggregateAssignmentOmitsSnapshotButKeepsDestinationStorage(
        WasmTarget target, CilOperation load, CilOperation store)
    {
        var fixture = new PlannerFixture(target);
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [CliValueKind.ValueType, CliValueKind.ValueType],
            [
                I(0, load, new CilOperand.Index(load == CilOperation.LoadArgument ? 1 : 0)),
                I(1, store, new CilOperand.Index(1)),
            ])
        {
            LocalSignatureTypes = [fixture.ValueType, fixture.ValueType],
        };

        var frame = ThroughContract(fixture.CreatePlanner()).Create(
            PlanningMethod(body, (0, 0), (1, 1)));

        Assert.Equal([0], frame.DirectValueLoadOffsets);
        Assert.Empty(frame.TemporaryOffsets);
        if (store == CilOperation.StoreLocal)
            Assert.Contains(1, frame.LocalOffsets.Keys);
        else
            Assert.Contains(1, frame.ArgumentOffsets.Keys);
        Assert.True(frame.Size >= fixture.Layouts.GetValueLayout(fixture.ValueType).Size);
    }

    [Fact]
    public void RejectsNullBodyAndRequiredDependencies()
    {
        var fixture = new PlannerFixture();
        var planner = ThroughContract(fixture.CreatePlanner());
        Assert.Throws<ArgumentNullException>(() => planner.Create(null!));

        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            null!,
            fixture,
            fixture.Layouts,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            null!,
            fixture.Layouts,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            null!,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            null!,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            null!,
            EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures,
            new DirectValueLoadSelector(),
            null!));
    }

    private static void AssertPlannerFailure(
        PlannerFixture fixture,
        CilOperation operation,
        string message)
    {
        var body = new CilMethodBody(
            fixture.BodyMethod,
            1,
            [],
            [I(0, operation)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ThroughContract(fixture.CreatePlanner()).Create(PlanningMethod(body)));

        Assert.Equal(message, exception.Message);
    }

    private static StructuredMethod PlanningMethod(
        CilMethodBody body,
        params (int InstructionOffset, int EntryCount)[] entryCounts)
    {
        var counts = entryCounts.ToImmutableDictionary(
            entry => entry.InstructionOffset,
            entry => entry.EntryCount);
        var entries = body.Instructions.ToImmutableDictionary(
            instruction => instruction.Offset,
            instruction => Enumerable.Repeat(
                    CliValueKind.I4,
                    counts.GetValueOrDefault(instruction.Offset))
                .ToImmutableArray());
        var blockId = new StructuredBlockId(0);
        var block = new StructuredBlockDefinition(
            blockId,
            body.Instructions.IsEmpty ? 0 : body.Instructions[0].Offset,
            body.Instructions,
            [],
            new StructuredFallthroughExit(null))
        {
            EndOffset = body.Instructions.IsEmpty ? 0 : body.Instructions[^1].NextOffset,
        };
        return new StructuredMethod(
            Header(body),
            blockId,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                .Add(blockId, block),
            new StructuredSequence([
                new StructuredCode(new StructuredBlockOccurrence(
                    blockId,
                    StructuredBlockRole.Owner)),
            ]),
            [],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
            entries);
    }

    private static IValueFrameLayoutPlanner ThroughContract(
        ValueFrameLayoutPlanner planner) =>
        new[] { planner }.Cast<IValueFrameLayoutPlanner>().Single();

    private static IDirectValueLoadSelector ThroughSelector(
        DirectValueLoadSelector selector) =>
        new[] { selector }.Cast<IDirectValueLoadSelector>().Single();

    private sealed class ArrayElementValueLayouts(CliTypeIdentity element, int referenceSize) :
        IValueLayoutProvider
    {
        public ValueLayout Layout { get; } = new(element, 3 * referenceSize, referenceSize, [0]);
        public List<CliTypeIdentity> Requests { get; } = [];

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            Requests.Add(type);
            Assert.Equal(element, type);
            return Layout;
        }
    }

    private sealed class PlannerFixture :
        IFieldRepository,
        IMethodRepository,
        ICilTypeIdentityResolver,
        ICilTypeOperandResolver,
        IArgumentTypeResolver,
        IArgumentSignatureTypeResolver
    {
        private static readonly EntityKey ValueTypeKey = Key(0x02000010);
        private static readonly EntityKey ReferenceTypeKey = Key(0x02000011);
        private static readonly EntityKey ValueMethodKey = Key(0x06000010);
        private static readonly EntityKey ScalarMethodKey = Key(0x06000011);
        private static readonly EntityKey ValueInstanceMethodKey = Key(0x06000012);
        private static readonly EntityKey ValueConstructorKey = Key(0x06000013);
        private static readonly EntityKey ReferenceConstructorKey = Key(0x06000014);
        private static readonly EntityKey BodyMethodKey = Key(0x06000015);
        private static readonly EntityKey ValueFieldKey = Key(0x04000010);
        private static readonly EntityKey ScalarFieldKey = Key(0x04000011);

        private readonly Dictionary<EntityKey, CliTypeIdentity> _types;
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods;
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields;

        public PlannerFixture(WasmTarget target = WasmTarget.Wasm32)
        {
            ValueType = CliTypeIdentity.Named(
                Assembly,
                "Test",
                "Value",
                isValueType: true);
            ReferenceType = CliTypeIdentity.Named(
                Assembly,
                "Test",
                "Reference",
                isValueType: false);
            var scalarType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
            var valueSignature = MethodSignatureModel.Create(ValueType);
            var scalarSignature = MethodSignatureModel.Create(scalarType);
            var bodySignature = MethodSignatureModel.Create(CliValueKind.Void);

            ValueStatic = new(
                ValueMethodKey,
                ReferenceTypeKey,
                "GetValue",
                true,
                valueSignature,
                1);
            ScalarStatic = new(
                ScalarMethodKey,
                ReferenceTypeKey,
                "GetScalar",
                true,
                scalarSignature,
                1);
            var valueInstance = new MethodDefinitionModel(
                ValueInstanceMethodKey,
                ValueTypeKey,
                "GetInstanceValue",
                false,
                valueSignature,
                1);
            ValueConstructor = new(
                ValueConstructorKey,
                ValueTypeKey,
                ".ctor",
                false,
                bodySignature,
                1);
            ReferenceConstructor = new(
                ReferenceConstructorKey,
                ReferenceTypeKey,
                ".ctor",
                false,
                bodySignature,
                1);
            BodyMethod = new(
                BodyMethodKey,
                ReferenceTypeKey,
                "Body",
                true,
                bodySignature,
                1);

            ValueStaticInstance = new(
                ValueStatic,
                ReferenceType,
                [],
                valueSignature);
            ValueInstance = new(
                valueInstance,
                ValueType,
                [],
                valueSignature);
            ScalarReceiverInstance = new(
                valueInstance,
                scalarType,
                [],
                scalarSignature);
            ReferenceInstance = new(
                ScalarStatic,
                ReferenceType,
                [],
                scalarSignature);
            ValueConstructorInstance = new(
                ValueConstructor,
                ValueType,
                [],
                bodySignature);

            ValueField = new(
                ValueFieldKey,
                ValueTypeKey,
                "Value",
                ValueType,
                false);
            ScalarField = new(
                ScalarFieldKey,
                ReferenceTypeKey,
                "Scalar",
                scalarType,
                false);

            _types = new()
            {
                [ValueTypeKey] = ValueType,
                [ReferenceTypeKey] = ReferenceType,
            };
            _methods = new()
            {
                [ValueMethodKey] = ValueStatic,
                [ScalarMethodKey] = ScalarStatic,
                [ValueInstanceMethodKey] = valueInstance,
                [ValueConstructorKey] = ValueConstructor,
                [ReferenceConstructorKey] = ReferenceConstructor,
            };
            _fields = new()
            {
                [ValueFieldKey] = ValueField,
                [ScalarFieldKey] = ScalarField,
            };
            Layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
            TypeIdentities = this;
            TypeOperands = this;
            ArgumentTypes = this;
            ArgumentSignatures = this;
        }

        public CliTypeIdentity ValueType { get; }
        public CliTypeIdentity ReferenceType { get; }
        public MethodDefinitionModel BodyMethod { get; }
        public MethodDefinitionModel ValueStatic { get; }
        public MethodDefinitionModel ScalarStatic { get; }
        public MethodDefinitionModel ValueConstructor { get; }
        public MethodDefinitionModel ReferenceConstructor { get; }
        public FieldDefinitionModel ValueField { get; }
        public FieldDefinitionModel ScalarField { get; }
        public MethodInstanceModel ValueStaticInstance { get; }
        public MethodInstanceModel ValueInstance { get; }
        public MethodInstanceModel ScalarReceiverInstance { get; }
        public MethodInstanceModel ReferenceInstance { get; }
        public MethodInstanceModel ValueConstructorInstance { get; }
        public RecordingLayoutProvider Layouts { get; }
        public ICilTypeIdentityResolver TypeIdentities { get; }
        public ICilTypeOperandResolver TypeOperands { get; }
        public IArgumentTypeResolver ArgumentTypes { get; }
        public IArgumentSignatureTypeResolver ArgumentSignatures { get; }

        public ValueFrameLayoutPlanner CreatePlanner(
            ITransientAggregatePromotionPlanner? promotions = null) => new(
            this,
            this,
            Layouts,
            Layouts,
            TypeOperands,
            TypeIdentities,
            ArgumentTypes,
            ArgumentSignatures,
            new DirectValueLoadSelector(),
            promotions ?? EmptyTransientAggregatePromotionPlanner.Instance);

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];
        public void AddMethod(MethodDefinitionModel method) => _methods.Add(method.Key, method);
        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
        public CliTypeIdentity Resolve(EntityKey key) => _types[key];
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            instruction.Operand switch
            {
                CilOperand.TypeIdentity identity => identity.Value,
                CilOperand.Entity entity => Resolve(entity.Key),
                _ => throw new InvalidOperationException(
                    $"instruction {instruction.Operation} has no type operand"),
            };

        public CliValueKind Resolve(StructuredMethodHeader header, int index) =>
            index == 1 ? CliValueKind.ValueType : CliValueKind.I4;

        public CliTypeIdentity ResolveSignature(StructuredMethodHeader header, int index) =>
            index == 1
                ? ValueType
                : CliTypeIdentity.FromStackKind(CliValueKind.I4);

        CliTypeIdentity IArgumentSignatureTypeResolver.Resolve(
            StructuredMethodHeader header,
            int index) => ResolveSignature(header, index);
    }

    private sealed class FixedTransientAggregatePromotionPlanner(
        TransientAggregatePromotionPlan plan) : ITransientAggregatePromotionPlanner
    {
        public TransientAggregatePromotionPlan Create(StructuredMethod method) => plan;
    }
}
