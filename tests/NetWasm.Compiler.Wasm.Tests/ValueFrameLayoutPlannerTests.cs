using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueFrameLayoutPlannerTests
{
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
