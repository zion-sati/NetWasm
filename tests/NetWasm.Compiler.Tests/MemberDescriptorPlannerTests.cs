using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MemberDescriptorPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("Fixture");
    private static readonly EntityKey DeclaringType = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity OwnerType = CliTypeIdentity.Named(
        Assembly,
        "Fixture",
        "Owner",
        isValueType: false);

    [Fact]
    public void BuildKeepsRootDescriptorsWithoutDemandingNamesOrProperties()
    {
        var element = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var generic = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                "Fixture",
                "Box`1",
                isValueType: false),
            [element]);
        var vector = CliTypeIdentity.SzArray(generic);
        var method = Method(1, "Read", vector);
        var field = Field(1, "Value", CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var properties = new RecordingPropertyAccessorResolver();
        var planner = new MemberDescriptorPlanner(properties);

        var plan = planner.Build(new MemberDescriptorPlanningRequest(
            ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                method.CanonicalName,
                method),
            ImmutableDictionary<string, FieldInstanceModel>.Empty.Add(
                field.CanonicalName,
                field),
            IncludePropertyAssociations: false,
            IncludeNames: false));

        Assert.Equal(method, Assert.Single(plan.Methods).Value);
        Assert.Equal(field, Assert.Single(plan.Fields).Value);
        Assert.Empty(plan.Properties);
        Assert.Empty(plan.NamedDescriptors);
        Assert.Empty(plan.NameStrings);
        Assert.Contains(OwnerType, plan.RequiredTypes);
        Assert.Contains(method.Signature.ReturnSignatureType, plan.RequiredTypes);
        Assert.Contains(generic, plan.RequiredTypes);
        Assert.Contains(element, plan.RequiredTypes);
        Assert.Contains(field.FieldType, plan.RequiredTypes);
        Assert.Empty(properties.Resolved);
    }

    [Fact]
    public void BuildClosesPropertyGraphAndDemandsOnlyItsMemberNames()
    {
        var getter = Method(1, "get_Value", CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var setter = Method(
            2,
            "set_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                new EntityKey(Assembly, 0x17000001),
                DeclaringType,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
                getter.Definition.Key,
                setter.Definition.Key),
            OwnerType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
            getter,
            setter);
        var properties = new RecordingPropertyAccessorResolver(property);
        var planner = new MemberDescriptorPlanner(properties);

        var plan = planner.Build(new MemberDescriptorPlanningRequest(
            ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                getter.CanonicalName,
                getter),
            ImmutableDictionary<string, FieldInstanceModel>.Empty,
            IncludePropertyAssociations: true,
            IncludeNames: true));

        Assert.Equal(2, plan.Methods.Count);
        Assert.Equal(property, Assert.Single(plan.Properties).Value);
        Assert.Equal(
            ["Value", "get_Value", "set_Value"],
            plan.NameStrings.Order(StringComparer.Ordinal));
        Assert.Equal(3, plan.NamedDescriptors.Count);
        Assert.Contains(
            CliTypeIdentity.FromStackKind(CliValueKind.I8),
            plan.RequiredTypes);
        Assert.Equal(
            [getter.Definition.Key, setter.Definition.Key],
            properties.Resolved.OrderBy(key => key.MetadataToken));
    }

    [Fact]
    public void BuildRejectsContradictoryPropertyIdentity()
    {
        var getter = Method(1, "get_Value", CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var setter = Method(
            2,
            "set_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var resolver = new ContradictoryPropertyAccessorResolver(getter, setter);
        var planner = new MemberDescriptorPlanner(resolver);

        var exception = Assert.Throws<CompilerException>(() => planner.Build(
            new MemberDescriptorPlanningRequest(
                ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    getter.CanonicalName,
                    getter),
                ImmutableDictionary<string, FieldInstanceModel>.Empty,
                IncludePropertyAssociations: true,
                IncludeNames: false)));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void BuildAcceptsPropertyWithoutSetter()
    {
        var getter = Method(1, "get_Value", CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                new EntityKey(Assembly, 0x17000001),
                DeclaringType,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                getter.Definition.Key,
                null),
            OwnerType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            getter,
            null);
        var planner = new MemberDescriptorPlanner(
            new RecordingPropertyAccessorResolver(property));

        var plan = planner.Build(new MemberDescriptorPlanningRequest(
            ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                getter.CanonicalName,
                getter),
            ImmutableDictionary<string, FieldInstanceModel>.Empty,
            IncludePropertyAssociations: true,
            IncludeNames: false));

        Assert.Single(plan.Methods);
        Assert.Same(property, Assert.Single(plan.Properties).Value);
    }

    [Fact]
    public void BuildRejectsContradictoryAccessorIdentity()
    {
        var getter = Method(1, "get_Value", CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var contradictory = getter with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.I8)),
        };
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                new EntityKey(Assembly, 0x17000001),
                DeclaringType,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                getter.Definition.Key,
                getter.Definition.Key),
            OwnerType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            getter,
            contradictory);
        var planner = new MemberDescriptorPlanner(
            new RecordingPropertyAccessorResolver(property));

        var exception = Assert.Throws<CompilerException>(() => planner.Build(
            new MemberDescriptorPlanningRequest(
                ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    getter.CanonicalName,
                    getter),
                ImmutableDictionary<string, FieldInstanceModel>.Empty,
                IncludePropertyAssociations: true,
                IncludeNames: false)));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void BuildRejectsNullRequest()
    {
        var planner = new MemberDescriptorPlanner(
            new RecordingPropertyAccessorResolver());

        Assert.Throws<ArgumentNullException>(() => planner.Build(null!));
        Assert.Throws<ArgumentNullException>(() => new MemberDescriptorPlanner(null!));
    }

    private static MethodInstanceModel Method(
        int row,
        string name,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000000 + row),
            DeclaringType,
            name,
            IsStatic: false,
            MethodSignatureModel.Create(returnType, parameters),
            RelativeVirtualAddress: 1);
        return new MethodInstanceModel(definition, OwnerType, [], definition.Signature);
    }

    private static FieldInstanceModel Field(
        int row,
        string name,
        CliTypeIdentity type)
    {
        var definition = new FieldDefinitionModel(
            new EntityKey(Assembly, 0x04000000 + row),
            DeclaringType,
            name,
            type,
            false);
        return new FieldInstanceModel(definition, OwnerType, type);
    }

    private sealed class RecordingPropertyAccessorResolver(
        PropertyInstanceModel? property = null) : IMetadataPropertyAccessorResolver
    {
        private readonly List<EntityKey> _resolved = [];

        public ImmutableArray<EntityKey> Resolved => [.. _resolved];

        public PropertyInstanceModel? Resolve(MethodInstanceModel accessor)
        {
            _resolved.Add(accessor.Definition.Key);
            return property;
        }
    }

    private sealed class ContradictoryPropertyAccessorResolver(
        MethodInstanceModel getter,
        MethodInstanceModel setter) : IMetadataPropertyAccessorResolver
    {
        public PropertyInstanceModel? Resolve(MethodInstanceModel accessor)
        {
            var name = accessor.Definition.Key == getter.Definition.Key
                ? "Value"
                : "OtherValue";
            return new PropertyInstanceModel(
                new PropertyDefinitionModel(
                    new EntityKey(Assembly, 0x17000001),
                    DeclaringType,
                    name,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                    [],
                    getter.Definition.Key,
                    setter.Definition.Key),
                OwnerType,
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                getter,
                setter);
        }
    }
}
