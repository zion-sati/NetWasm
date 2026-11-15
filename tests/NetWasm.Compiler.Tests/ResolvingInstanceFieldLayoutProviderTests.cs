// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ResolvingInstanceFieldLayoutProviderTests
{
    [Fact]
    public void ResolvesMissingOrdinaryValueFieldThroughWorkerResolver()
    {
        var fixture = new Fixture();
        var instance = new FieldInstanceModel(fixture.ValueField,
            fixture.ValueIdentity, fixture.ValueIdentity);

        var layout = fixture.Create().GetFieldLayout(instance);

        Assert.Equal(8, layout.Offset);
        Assert.Equal(1, fixture.Resolver.Calls);
        Assert.Same(fixture.ValueType, fixture.Resolver.SuppliedDefinition);
        Assert.Equal(0, fixture.Inner.InstanceCalls);
    }

    [Fact]
    public void ResolvesConstructedValueFieldWithoutOrdinaryLookup()
    {
        var fixture = new Fixture();
        var instance = new FieldInstanceModel(fixture.ValueField,
            CliTypeIdentity.GenericInstantiation(fixture.ValueIdentity,
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)]),
            fixture.ValueIdentity);

        var layout = fixture.Create().GetFieldLayout(instance);

        Assert.Equal(8, layout.Offset);
        Assert.Equal(1, fixture.Resolver.Calls);
        Assert.Null(fixture.Resolver.SuppliedDefinition);
        Assert.Equal(2, fixture.Inner.InstanceCalls);
    }

    [Fact]
    public void ExistingLayoutDoesNotResolveAgain()
    {
        var fixture = new Fixture();
        fixture.Inner.AlwaysAvailable = true;

        Assert.Equal(8, fixture.Create().GetFieldLayout(
            fixture.ValueField.Key).Offset);
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Fact]
    public void RejectsClassAndStaticFieldsWithoutChangingMissingDiagnostic()
    {
        var fixture = new Fixture();
        var adapter = fixture.Create();

        Assert.Same(fixture.Inner.Missing,
            Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                fixture.ClassField.Key)));
        Assert.Same(fixture.Inner.Missing,
            Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                fixture.StaticField.Key)));
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Fact]
    public void RejectsConstructedReferenceAndStaticFields()
    {
        var fixture = new Fixture();
        var reference = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.FromDefinition(fixture.ClassType),
            [fixture.ValueIdentity]);
        var adapter = fixture.Create();

        Assert.Same(fixture.Inner.Missing,
            Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                new FieldInstanceModel(fixture.ClassField, reference,
                    fixture.ValueIdentity))));
        Assert.Same(fixture.Inner.Missing,
            Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                new FieldInstanceModel(fixture.StaticField,
                    fixture.ValueIdentity, fixture.ValueIdentity))));
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRecoveryPreservesOriginalMissingDiagnostic(bool constructed)
    {
        var fixture = new Fixture();
        fixture.Resolver.Failure = new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract, "recovery failed"));
        var adapter = fixture.Create();

        var failure = constructed
            ? Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                new FieldInstanceModel(fixture.ValueField,
                    CliTypeIdentity.GenericInstantiation(fixture.ValueIdentity,
                        [CliTypeIdentity.FromStackKind(CliValueKind.I4)]),
                    fixture.ValueIdentity)))
            : Assert.Throws<CompilerException>(() => adapter.GetFieldLayout(
                fixture.ValueField.Key));
        Assert.Same(fixture.Inner.Missing, failure);
    }

    [Fact]
    public void CancellationFromResolverIsNotHidden()
    {
        var fixture = new Fixture();
        var canceled = new OperationCanceledException("stop");
        fixture.Resolver.Failure = canceled;

        Assert.Same(canceled,
            Assert.Throws<OperationCanceledException>(() =>
                fixture.Create().GetFieldLayout(fixture.ValueField.Key)));
    }

    [Fact]
    public void RejectsNullConstructedFieldArgument()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() =>
            fixture.Create().GetFieldLayout((FieldInstanceModel)null!));
    }

    private sealed class Fixture
    {
        private readonly AssemblyIdentity _assembly = new("LayoutForkTests");
        public TypeDefinitionModel ClassType { get; }
        public TypeDefinitionModel ValueType { get; }
        public FieldDefinitionModel ClassField { get; }
        public FieldDefinitionModel ValueField { get; }
        public FieldDefinitionModel StaticField { get; }
        public CliTypeIdentity ValueIdentity => CliTypeIdentity.FromDefinition(ValueType);
        public FakeResolver Resolver { get; } = new();
        public FakeInner Inner { get; }

        public Fixture()
        {
            var classKey = new EntityKey(_assembly, 1);
            var valueKey = new EntityKey(_assembly, 2);
            var classFieldKey = new EntityKey(_assembly, 3);
            var valueFieldKey = new EntityKey(_assembly, 4);
            var staticFieldKey = new EntityKey(_assembly, 5);
            ClassType = new(classKey, "", "ReferenceBox", false,
                [classFieldKey], []);
            ValueType = new(valueKey, "", "ValueBox", true,
                [valueFieldKey, staticFieldKey], []);
            ClassField = new(classFieldKey, classKey, "Value",
                CliValueKind.ManagedReference, false);
            ValueField = new(valueFieldKey, valueKey, "Value",
                CliValueKind.ManagedReference, false);
            StaticField = new(staticFieldKey, valueKey, "StaticValue",
                CliValueKind.ManagedReference, true);
            Inner = new(Resolver);
        }

        public ResolvingInstanceFieldLayoutProvider Create() => new(
            Inner,
            new FakeTypes(ClassType, ValueType),
            new FakeFields(ClassField, ValueField, StaticField),
            Resolver);
    }

    private sealed class FakeTypes(params TypeDefinitionModel[] items) :
        ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            items.Single(item => item.Key == key);
    }

    private sealed class FakeFields(params FieldDefinitionModel[] items) :
        IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            items.Single(item => item.Key == key);
    }

    private sealed class FakeResolver : IValueLayoutResolver
    {
        public int Calls { get; private set; }
        public TypeDefinitionModel? SuppliedDefinition { get; private set; }
        public Exception? Failure { get; set; }

        public ValueLayout Resolve(CliTypeIdentity type)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return new(type, 8, 8, []);
        }

        public ValueLayout Resolve(CliTypeIdentity type,
            TypeDefinitionModel definition)
        {
            SuppliedDefinition = definition;
            return Resolve(type);
        }
    }

    private sealed class FakeInner(FakeResolver resolver) :
        IInstanceFieldLayoutProvider
    {
        public CompilerException Missing { get; } = new(new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract, "missing field layout"));
        public bool AlwaysAvailable { get; set; }
        public int InstanceCalls { get; private set; }

        public FieldLayout GetFieldLayout(EntityKey field) =>
            AlwaysAvailable || resolver.Calls > 0 && field.MetadataToken == 4
                ? new(8)
                : throw Missing;

        public FieldLayout GetFieldLayout(FieldInstanceModel field)
        {
            InstanceCalls++;
            return AlwaysAvailable || resolver.Calls > 0
                ? new(8)
                : throw Missing;
        }
    }
}
