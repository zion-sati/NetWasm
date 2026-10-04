using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueFrameLayoutPlannerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MemberCallsReserveDistinctAlignedScratchForTheirLargestResult(WasmTarget target)
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
            fixture.ArgumentTypes, fixture.ArgumentSignatures);
        var body = new CilMethodBody(fixture.BodyMethod, 1, [],
        [
            I(0, CilOperation.LocalAllocate),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(invoker)),
            I(7, CilOperation.Call, new CilOperand.Entity(invoker.Definition.Key)),
            I(11, CilOperation.Call, new CilOperand.MethodInstance(fixture.ReferenceInstance)),
        ]);

        var frame = ThroughContract(planner).Create(Header(body), memberExecution: plan);

        Assert.Equal(2, frame.MemberResultOffsets.Count);
        Assert.Equal(8, frame.MemberResultOffsets[3]);
        Assert.Equal(24, frame.MemberResultOffsets[7]);
        Assert.Equal(40, frame.Size);
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
            fixture.ArgumentTypes, fixture.ArgumentSignatures);

        var frame = ThroughContract(planner).Create(Header(body), memberExecution: plan);

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
            fixture.ArgumentTypes, fixture.ArgumentSignatures);

        var error = Assert.Throws<CompilerException>(() => ThroughContract(planner).Create(
            Header(body), memberExecution: MemberPlan(invoker, nullable)));

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
    public void NativeCallsReserveDistinctAlignedCopiesAndResultsFromThePublishedPlan(WasmTarget target)
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

        var frame = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(Header(body), imports);

        Assert.Equal(4, frame.NativeArgumentOffsets[(3, 0)]);
        Assert.Equal(16, frame.NativeArgumentOffsets[(3, 1)]);
        Assert.Equal(32, frame.TemporaryOffsets[3]);
        Assert.Equal(48, frame.NativeArgumentOffsets[(7, 0)]);
        Assert.Equal(60, frame.NativeArgumentOffsets[(7, 1)]);
        Assert.Equal(80, frame.TemporaryOffsets[7]);
        Assert.Equal(96, frame.Size);
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

        var frame = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(Header(body), imports);

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
            ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(Header(body), imports));

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
            Assert.Throws<CompilerException>(() => planner.Create(Header(body))).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.CompilerInvariant,
            Assert.Throws<CompilerException>(() => planner.Create(Header(body), NativeImportPlan.Empty)).Diagnostic.Code);
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
    public void ArrayValueCopiesReserveDistinctAlignedElementStorage(
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
            CreateArgumentTypes(program), CreateArgumentSignatureTypes(program)));
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

        var layout = planner.Create(Header(body));

        var alignment = layouts.Target.ObjectReferenceAlignment;
        Assert.Equal(2, layout.TemporaryOffsets.Count);
        Assert.Equal(alignment, layout.TemporaryOffsets[3]);
        Assert.Equal(alignment + values.Layout.Size, layout.TemporaryOffsets[7]);
        Assert.Equal(alignment + 2 * values.Layout.Size, layout.Size);
        Assert.Equal([valueType, valueType], values.Requests);
        Assert.Empty(layout.LocalOffsets);
        Assert.Empty(layout.ArgumentOffsets);

        CilOperand.TypeIdentity Operand(CliTypeIdentity element) => new(
            rank == 0 ? element : CliTypeIdentity.Array(element, rank));
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

        var layout = ThroughContract(CreateValueFrameLayoutPlanner(program, layouts)).Create(Header(body));

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
            planner.Create(Header(body));
        var layout = contract(CreateValueFrameLayoutPlanner(program, layouts));

        Assert.Equal(20, layout.Size);
        Assert.Equal(4, layout.ArgumentOffsets[0]);
        Assert.Equal(8, layout.LocalOffsets[0]);
        Assert.Equal(12, layout.LocalOffsets[1]);
        Assert.Equal(16, layout.TemporaryOffsets[3]);
        Assert.Equal([0], layout.SpilledScalarLocals);
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

        var layout = planner.Create(Header(body));

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

        var layout = ThroughContract(fixture.CreatePlanner()).Create(Header(body));

        Assert.Contains(0, layout.ArgumentOffsets.Keys);
        Assert.Contains(0, layout.LocalOffsets.Keys);
        Assert.Contains(1, layout.LocalOffsets.Keys);
        Assert.Contains(0, layout.SpilledScalarLocals);
        foreach (var offset in new[]
        {
            4, 6, 8, 10, 12, 14, 16, 18, 20, 21, 23, 25, 26, 28,
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
        Assert.DoesNotContain(22, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(24, layout.TemporaryOffsets.Keys);
        Assert.DoesNotContain(27, layout.TemporaryOffsets.Keys);
        Assert.True(layout.Size > layout.TemporaryOffsets.Count * 4);
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
            fixture.ArgumentSignatures));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            null!,
            fixture.Layouts,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            null!,
            fixture.Layouts,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures));
        Assert.Throws<ArgumentNullException>(() => new ValueFrameLayoutPlanner(
            fixture,
            fixture,
            fixture.Layouts,
            null!,
            fixture.TypeOperands,
            fixture.TypeIdentities,
            fixture.ArgumentTypes,
            fixture.ArgumentSignatures));
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
            ThroughContract(fixture.CreatePlanner()).Create(Header(body)));

        Assert.Equal(message, exception.Message);
    }

    private static IValueFrameLayoutPlanner ThroughContract(
        ValueFrameLayoutPlanner planner) =>
        new[] { planner }.Cast<IValueFrameLayoutPlanner>().Single();

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

        public PlannerFixture()
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
            Layouts = new RecordingLayoutProvider();
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

        public ValueFrameLayoutPlanner CreatePlanner() => new(
            this,
            this,
            Layouts,
            Layouts,
            TypeOperands,
            TypeIdentities,
            ArgumentTypes,
            ArgumentSignatures);

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
}
