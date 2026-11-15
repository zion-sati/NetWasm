// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorConstraintValidatorTests
{
    private const GenericParameterAttributes Reference = GenericParameterAttributes.ReferenceTypeConstraint;
    private const GenericParameterAttributes Value = GenericParameterAttributes.NotNullableValueTypeConstraint;
    private const GenericParameterAttributes Constructor = GenericParameterAttributes.DefaultConstructorConstraint;
    private static readonly CliTypeIdentity Object = Type("Object");
    private static readonly CliTypeIdentity ValueType = Type("ValueType");
    private static readonly CliTypeIdentity Enum = Type("Enum");
    private static readonly CliTypeIdentity Interface = Type("IFoo");
    private static readonly CliTypeIdentity DerivedInterface = Type("IDerivedFoo");
    private static readonly CliTypeIdentity OtherInterface = Type("IBar");
    private static readonly CliTypeIdentity ReferenceClass = Type("Base");
    private static readonly CliTypeIdentity Struct = Type("Struct", true);
    private static readonly CliTypeIdentity Nullable = Type("Nullable`1", true);
    private static readonly CliTypeIdentity ForeignNullable = CliTypeIdentity.Named(new("Foreign"), "System", "Nullable`1", true);
    private static readonly CliTypeIdentity PrivateConstructorStruct = Type("PrivateConstructorStruct", true);
    private static readonly CliTypeIdentity PublicConstructorStruct = Type("PublicConstructorStruct", true);

    [Theory]
    [InlineData(Reference, Reference, true)]
    [InlineData(Reference | Constructor, Reference, true)]
    [InlineData(GenericParameterAttributes.None, Reference, false)]
    [InlineData(Value, Reference, false)]
    [InlineData(Value, Value, true)]
    [InlineData(Reference, Value, false)]
    [InlineData(Constructor, Value, false)]
    [InlineData(Value, Constructor, true)]
    [InlineData(Constructor, Constructor, true)]
    [InlineData(GenericParameterAttributes.None, Constructor, false)]
    [InlineData(Reference, GenericParameterAttributes.None, true)]
    [InlineData(Value | Constructor, Value | Constructor, true)]
    public void SpecialConstraintsRequireImplicationInsteadOfFlagEquality(
        GenericParameterAttributes declared, GenericParameterAttributes required, bool expected)
    {
        var fixture = new Fixture();
        Assert.Equal(expected, fixture.Create().Validate(MethodParameters(Parameter(declared)), MethodParameters(Parameter(required))));
        Assert.Equal(expected, fixture.Create().Validate(TypeParameters(Parameter(declared)), TypeParameters(Parameter(required))));
    }

    [Theory]
    [InlineData("class", Reference, true)]
    [InlineData("interface", Reference, false)]
    [InlineData("object", Reference, false)]
    [InlineData("value-base", Reference, false)]
    [InlineData("enum", Reference, false)]
    [InlineData("struct", Reference, false)]
    [InlineData("class", Value, false)]
    [InlineData("struct", Value, true)]
    [InlineData("nullable", Value, false)]
    [InlineData("foreign-nullable", Value, true)]
    [InlineData("class", Constructor, false)]
    [InlineData("struct", Constructor, true)]
    [InlineData("public-constructor", Constructor, true)]
    [InlineData("private-constructor", Constructor, false)]
    public void NominalConstraintsContributeOnlyTheirGuaranteedSpecialFacts(
        string shape, GenericParameterAttributes required, bool expected)
    {
        var constraint = shape switch
        {
            "class" => ReferenceClass,
            "interface" => Interface,
            "object" => Object,
            "value-base" => ValueType,
            "enum" => Enum,
            "struct" => Struct,
            "nullable" => Nullable,
            "foreign-nullable" => ForeignNullable,
            "public-constructor" => PublicConstructorStruct,
            "private-constructor" => PrivateConstructorStruct,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var fixture = new Fixture();
        Assert.Equal(expected, fixture.Create().Validate(MethodParameters(Parameter(0, constraint)), MethodParameters(Parameter(required))));
    }

    [Theory]
    [InlineData(Reference, Reference, false)]
    [InlineData(Constructor, Constructor, false)]
    [InlineData(Value, Constructor, true)]
    [InlineData(Value, Value, true)]
    public void VariableChainsDoNotInheritClassOrConstructorFlags(
        GenericParameterAttributes inherited, GenericParameterAttributes required, bool expected)
    {
        var fixture = new Fixture();
        var accessor = MethodParameters(Parameter(0, Variable(1)), Parameter(inherited));
        var target = MethodParameters(Parameter(required), Parameter());
        Assert.Equal(expected, fixture.Create().Validate(accessor, target));
    }

    [Fact]
    public void VariableChainsCanImplyAClassThroughNominalAncestry()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Create().Validate(
            MethodParameters(Parameter(0, Variable(1)), Parameter(0, ReferenceClass)),
            MethodParameters(Parameter(Reference), Parameter())));
    }

    [Fact]
    public void GeneralConstraintsAcceptReorderingStrongerInterfacesAndObject()
    {
        var fixture = new Fixture();
        var declared = Parameter(0, OtherInterface, DerivedInterface);
        var target = Parameter(0, Interface, OtherInterface, Object);
        Assert.True(fixture.Create().Validate(MethodParameters(declared), MethodParameters(target)));
        Assert.True(fixture.Create().Validate(TypeParameters(declared), TypeParameters(target)));
        Assert.False(fixture.Create().Validate(MethodParameters(Parameter(0, OtherInterface)), MethodParameters(target)));
    }

    [Fact]
    public void StructFlagsAndNominalValueTypesSatisfyValueTypeConstraint()
    {
        var fixture = new Fixture();
        var target = MethodParameters(Parameter(0, ValueType));
        Assert.True(fixture.Create().Validate(MethodParameters(Parameter(Value)), target));
        Assert.True(fixture.Create().Validate(MethodParameters(Parameter(0, Struct)), target));
        Assert.False(fixture.Create().Validate(MethodParameters(Parameter()), target));
    }

    [Fact]
    public void TypeAndMethodVariablesKeepTheirOriginalFormAndIndex()
    {
        var fixture = new Fixture();
        var accessor = new UnsafeAccessorGenericConstraints([Parameter(0, Interface)], [Parameter(0, TypeVariable(0))]);
        var compatible = new UnsafeAccessorGenericConstraints([Parameter()], [Parameter(0, Interface, TypeVariable(0))]);
        Assert.True(fixture.Create().Validate(accessor, compatible));
        Assert.False(fixture.Create().Validate(MethodParameters(Parameter()), MethodParameters(Parameter(0, TypeVariable(0)))));
        Assert.False(fixture.Create().Validate(MethodParameters(Parameter(), Parameter()), MethodParameters(Parameter(0, Variable(1)), Parameter())));
        Assert.True(fixture.Create().Validate(MethodParameters(Parameter(0, Variable(1)), Parameter()), MethodParameters(Parameter(0, Variable(1)), Parameter())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetGroupsRequireMatchingArityOnlyWhenPresent(bool methods)
    {
        var fixture = new Fixture();
        var empty = new UnsafeAccessorGenericConstraints([], []);
        var one = methods ? MethodParameters(Parameter()) : TypeParameters(Parameter());
        var two = methods ? MethodParameters(Parameter(), Parameter()) : TypeParameters(Parameter(), Parameter());
        Assert.False(fixture.Create().Validate(empty, one));
        Assert.False(fixture.Create().Validate(two, one));
        Assert.True(fixture.Create().Validate(one, empty));
    }

    [Fact]
    public void CyclesTerminateWithoutDiscardingReachableConstraintEvidence()
    {
        var fixture = new Fixture();
        var cycle = MethodParameters(Parameter(0, Variable(1)), Parameter(0, Variable(0)));
        Assert.False(fixture.Create().Validate(cycle, MethodParameters(Parameter(0, Interface), Parameter())));
        Assert.False(fixture.Create().Validate(cycle, MethodParameters(Parameter(Reference), Parameter())));
        var anchored = MethodParameters(Parameter(0, Variable(1)), Parameter(0, Variable(0), ReferenceClass, DerivedInterface));
        Assert.True(fixture.Create().Validate(anchored, MethodParameters(Parameter(Reference, Interface), Parameter())));
    }

    [Fact]
    public void UnboundConstraintVariablesCannotSupplySpecialOrGeneralProof()
    {
        var fixture = new Fixture();
        var accessor = MethodParameters(Parameter(0, Variable(99)));
        Assert.False(fixture.Create().Validate(accessor, MethodParameters(Parameter(Reference))));
        Assert.False(fixture.Create().Validate(accessor, MethodParameters(Parameter(0, ValueType))));
    }

    [Fact]
    public void MissingModelsFailAtTheContractBoundary()
    {
        var fixture = new Fixture();
        var validator = fixture.Create();
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, MethodParameters()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(MethodParameters(), null!));
    }

    private static CliTypeIdentity Type(string name, bool value = false) => CliTypeIdentity.Named(new("Core"), "System", name, value);
    private static CliTypeIdentity Variable(int index) => CliTypeIdentity.GenericParameter(true, index);
    private static CliTypeIdentity TypeVariable(int index) => CliTypeIdentity.GenericParameter(false, index);
    private static UnsafeAccessorGenericParameterConstraints Parameter(GenericParameterAttributes flags = 0, params CliTypeIdentity[] types) => new(flags, [.. types]);
    private static UnsafeAccessorGenericConstraints MethodParameters(params UnsafeAccessorGenericParameterConstraints[] parameters) => new([], [.. parameters]);
    private static UnsafeAccessorGenericConstraints TypeParameters(params UnsafeAccessorGenericParameterConstraints[] parameters) => new([.. parameters], []);

    private sealed class Fixture : IUnsafeAccessorSignatureComparer, IUnsafeAccessorTypeAssignability, ITypeDefinitionResolver, IMethodRepository
    {
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _types = [];
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = [];

        internal Fixture()
        {
            foreach (var type in new[] { Object, ValueType, Enum, Interface, DerivedInterface, OtherInterface,
                ReferenceClass, Struct, Nullable, ForeignNullable, PrivateConstructorStruct, PublicConstructorStruct })
            {
                var key = new EntityKey(type.Assembly ?? new("Core"), 0x02000001 + _types.Count);
                var methods = ImmutableArray<EntityKey>.Empty;
                if (type.Equals(Struct))
                {
                    // Neither a static initializer nor an argument-taking constructor
                    // removes the value type's implicit public default constructor.
                    methods = [AddMethod(key, ".cctor", true, false, []), AddMethod(key, ".ctor", false, false, [Object]),
                        AddMethod(key, "Other", false, false, [])];
                }
                if (type.Equals(PrivateConstructorStruct) || type.Equals(PublicConstructorStruct))
                    methods = [AddMethod(key, ".ctor", false, type.Equals(PublicConstructorStruct), [])];
                _types.Add(type, new(key, "System", type.FullName is { } fullName ? fullName[7..] : "Object", type.IsValueType, [], methods)
                { IsInterface = type.Equals(Interface) || type.Equals(DerivedInterface) || type.Equals(OtherInterface) });
            }
        }

        internal IUnsafeAccessorConstraintValidator Create() => ThroughContract(new UnsafeAccessorConstraintValidator(this, this, this, this, Object, ValueType));
        private static IUnsafeAccessorConstraintValidator ThroughContract(IUnsafeAccessorConstraintValidator validator) => validator;
        public bool Compare(CliTypeIdentity left, CliTypeIdentity right, bool includeModifiers)
        {
            Assert.False(includeModifiers);
            return left.Equals(right);
        }
        public bool IsAssignable(CliTypeIdentity source, CliTypeIdentity target, UnsafeAccessorGenericConstraints constraints) => source.Equals(target) ||
            source.Equals(DerivedInterface) && target.Equals(Interface) ||
            (source.IsValueType || source.Equals(Enum)) && target.Equals(ValueType);
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _types[identity];
        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

        private EntityKey AddMethod(EntityKey owner, string name, bool isStatic, bool isPublic, ImmutableArray<CliTypeIdentity> parameters)
        {
            var key = new EntityKey(owner.Assembly, 0x06000001 + _methods.Count);
            _methods.Add(key, new(key, owner, name, isStatic, new(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameters), 1)
            { IsPublic = isPublic });
            return key;
        }
    }
}
