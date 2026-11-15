// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class UnsafeAccessorTypeAssignabilityTests
{
    [Fact]
    public void FollowsNominalBaseAndInterfaceConstraintsWithoutConfusingAssemblies()
    {
        var fixture = new Fixture();
        var contract = fixture.Add("IContract", isInterface: true);
        var baseType = fixture.Add("Base", parent: fixture.Object);
        var derived = fixture.Add("Derived", parent: baseType, interfaces: [contract]);
        var impostor = fixture.Add("IContract", isInterface: true, assembly: "Other");
        var comparer = fixture.Actor();
        Assert.True(comparer.IsAssignable(derived, baseType, Empty));
        Assert.True(comparer.IsAssignable(derived, contract, Empty));
        Assert.True(comparer.IsAssignable(derived, fixture.Object, Empty));
        Assert.False(comparer.IsAssignable(derived, impostor, Empty));
        Assert.False(comparer.IsAssignable(baseType, derived, Empty));
        Assert.True(comparer.IsAssignable(derived, derived, Empty));
    }

    [Theory]
    [InlineData(CliGenericVariance.Covariant, true, false)]
    [InlineData(CliGenericVariance.Contravariant, false, true)]
    [InlineData(CliGenericVariance.Invariant, false, false)]
    public void GenericVarianceRequiresCorrectDirectionAndReferenceArguments(CliGenericVariance variance, bool forwards, bool backwards)
    {
        var fixture = new Fixture();
        var parent = fixture.Add("Parent", parent: fixture.Object);
        var child = fixture.Add("Child", parent: parent);
        var contract = fixture.Add("Contract`1", isInterface: true, variances: [variance]);
        var childContract = CliTypeIdentity.GenericInstantiation(contract, [child]);
        var parentContract = CliTypeIdentity.GenericInstantiation(contract, [parent]);
        var actor = fixture.Actor();
        Assert.Equal(forwards, actor.IsAssignable(childContract, parentContract, Empty));
        Assert.Equal(backwards, actor.IsAssignable(parentContract, childContract, Empty));
        var value = fixture.Add("Value", isValueType: true, parent: fixture.ValueType);
        Assert.False(actor.IsAssignable(CliTypeIdentity.GenericInstantiation(contract, [value]),
            CliTypeIdentity.GenericInstantiation(contract, [fixture.Object]), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.GenericInstantiation(contract, [fixture.Object]),
            CliTypeIdentity.GenericInstantiation(contract, [value]), Empty));
    }

    [Fact]
    public void VarianceCanUseAnOpenVariablesReferenceConstraint()
    {
        var fixture = new Fixture();
        var contract = fixture.Add("IEnumerable`1", isInterface: true, variances: [CliGenericVariance.Covariant]);
        var variable = CliTypeIdentity.GenericParameter(false, 0);
        var from = CliTypeIdentity.GenericInstantiation(contract, [variable]);
        var to = CliTypeIdentity.GenericInstantiation(contract, [fixture.Object]);
        var constrained = new UnsafeAccessorGenericConstraints([new(GenericParameterAttributes.ReferenceTypeConstraint, [])], []);
        var unconstrained = new UnsafeAccessorGenericConstraints([new(GenericParameterAttributes.None, [])], []);
        Assert.True(fixture.Actor().IsAssignable(from, to, constrained));
        Assert.False(fixture.Actor().IsAssignable(from, to, unconstrained));
    }

    [Fact]
    public void VariablesFollowTheirOwnKindAndConstraintGraphAndTerminateCycles()
    {
        var fixture = new Fixture();
        var bound = fixture.Add("Bound", parent: fixture.Object);
        var other = fixture.Add("Other");
        var typeVariable = CliTypeIdentity.GenericParameter(false, 0);
        var methodVariable = CliTypeIdentity.GenericParameter(true, 0);
        var constraints = new UnsafeAccessorGenericConstraints(
            [new(GenericParameterAttributes.None, [methodVariable])],
            [new(GenericParameterAttributes.None, [typeVariable, bound])]);
        var actor = fixture.Actor();
        Assert.True(actor.IsAssignable(typeVariable, bound, constraints));
        Assert.True(actor.IsAssignable(methodVariable, typeVariable, constraints));
        Assert.False(actor.IsAssignable(typeVariable, other, constraints));
        Assert.False(actor.IsAssignable(CliTypeIdentity.GenericParameter(true, 1), bound, constraints));
        Assert.False(actor.IsAssignable(typeVariable, fixture.ValueType, constraints));
        var value = new UnsafeAccessorGenericConstraints([new(GenericParameterAttributes.NotNullableValueTypeConstraint, [])], []);
        Assert.True(actor.IsAssignable(typeVariable, fixture.ValueType, value));
    }

    [Fact]
    public void VarianceFollowsNominalReferenceBoundsButNotAnInterfaceOrTransitiveClassFlag()
    {
        var fixture = new Fixture();
        var bound = fixture.Add("Bound", parent: fixture.Object);
        var contract = fixture.Add("IContract", isInterface: true);
        var generic = fixture.Add("Generic`1", isInterface: true, variances: [CliGenericVariance.Covariant]);
        var variable = CliTypeIdentity.GenericParameter(false, 0);
        var second = CliTypeIdentity.GenericParameter(false, 1);
        var from = CliTypeIdentity.GenericInstantiation(generic, [variable]);
        var to = CliTypeIdentity.GenericInstantiation(generic, [fixture.Object]);
        UnsafeAccessorGenericConstraints Constraints(CliTypeIdentity constraint, GenericParameterAttributes flags = GenericParameterAttributes.None) =>
            new([new(GenericParameterAttributes.None, [second]), new(flags, [constraint])], []);
        Assert.True(fixture.Actor().IsAssignable(from, to, Constraints(bound)));
        Assert.False(fixture.Actor().IsAssignable(from, to, Constraints(contract)));
        Assert.False(fixture.Actor().IsAssignable(from, to, Constraints(fixture.ValueType)));
        Assert.False(fixture.Actor().IsAssignable(from, to, Constraints(fixture.Object, GenericParameterAttributes.ReferenceTypeConstraint)));
        Assert.False(fixture.Actor().IsAssignable(from, to, Constraints(variable)));
    }

    [Fact]
    public void ArraysAreCovariantOnlyWithMatchingShapeAndReferenceElements()
    {
        var fixture = new Fixture();
        var parent = fixture.Add("Parent");
        var child = fixture.Add("Child", parent: parent);
        var value = fixture.Add("Value", isValueType: true);
        var actor = fixture.Actor();
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(child), CliTypeIdentity.SzArray(parent), Empty));
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(child), CliTypeIdentity.Array(parent, 1), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.Array(child, 2), CliTypeIdentity.Array(parent, 3), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(value), CliTypeIdentity.SzArray(parent), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(child), CliTypeIdentity.SzArray(value), Empty));
    }

    [Fact]
    public void ArrayInterfaceBoundsComposeWithOuterGenericVariance()
    {
        var fixture = new Fixture();
        var text = fixture.Add("Text", parent: fixture.Object);
        var enumerable = fixture.Add("IEnumerable`1", isInterface: true, variances: [CliGenericVariance.Covariant],
            typeNamespace: "System.Collections.Generic");
        var list = fixture.Add("IReadOnlyList`1", isInterface: true, variances: [CliGenericVariance.Covariant],
            typeNamespace: "System.Collections.Generic");
        var from = CliTypeIdentity.GenericInstantiation(enumerable, [CliTypeIdentity.SzArray(text)]);
        var to = CliTypeIdentity.GenericInstantiation(enumerable, [CliTypeIdentity.GenericInstantiation(list, [text])]);
        Assert.True(fixture.Actor().IsAssignable(from, to, Empty));
        var impostor = fixture.Add("IReadOnlyList`1", isInterface: true, variances: [CliGenericVariance.Covariant],
            typeNamespace: "System.Collections.Generic", assembly: "Other");
        Assert.False(fixture.Actor().IsAssignable(CliTypeIdentity.SzArray(text), CliTypeIdentity.GenericInstantiation(impostor, [text]), Empty));
        Assert.True(fixture.Actor().IsAssignable(CliTypeIdentity.Array(text, 2), fixture.Array, Empty));
    }

    [Fact]
    public void InvalidInputsAndMalformedVarianceFailExplicitly()
    {
        var fixture = new Fixture();
        var actor = fixture.Actor();
        Assert.Throws<ArgumentNullException>(() => actor.IsAssignable(null!, fixture.Object, Empty));
        Assert.Throws<ArgumentNullException>(() => actor.IsAssignable(fixture.Object, null!, Empty));
        Assert.Throws<ArgumentNullException>(() => actor.IsAssignable(fixture.Object, fixture.Object, null!));
        var generic = fixture.Add("Broken`1");
        var argument = fixture.Add("Argument");
        Assert.Throws<BadImageFormatException>(() => actor.IsAssignable(
            CliTypeIdentity.GenericInstantiation(generic, [argument]), CliTypeIdentity.GenericInstantiation(generic, [fixture.Object]), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.UnmanagedPointer(argument), fixture.Object, Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.ManagedByReference(argument), fixture.Object, Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.FromStackKind(CliValueKind.Void), fixture.Object, Empty));
    }

    [Theory]
    [InlineData("i1", "u1", CliValueKind.I4)]
    [InlineData("i2", "u2", CliValueKind.I4)]
    [InlineData("i4", "u4", CliValueKind.I4)]
    [InlineData("i8", "u8", CliValueKind.I8)]
    [InlineData("nativeint", "nativeuint", CliValueKind.NativeInt)]
    public void ArrayStoragePairsWorkInBothDirectionsIncludingEnumElements(string signedName, string unsignedName, CliValueKind stackKind)
    {
        var fixture = new Fixture();
        var signed = fixture.AddPrimitive(signedName, stackKind);
        var unsigned = fixture.AddPrimitive(unsignedName, stackKind);
        var enumeration = fixture.Add("Code", isValueType: true).WithStackStorageType(signed);
        var actor = fixture.Actor();
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(signed), CliTypeIdentity.SzArray(unsigned), Empty));
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(unsigned), CliTypeIdentity.SzArray(signed), Empty));
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(enumeration), CliTypeIdentity.SzArray(unsigned), Empty));
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(unsigned), CliTypeIdentity.SzArray(enumeration), Empty));
    }

    [Fact]
    public void ArrayStorageCompatibilityDoesNotGeneralizeToEqualWidthOrDifferentRanks()
    {
        var fixture = new Fixture();
        var byteType = fixture.AddPrimitive("u1", CliValueKind.I4);
        var boolean = fixture.AddPrimitive("bool", CliValueKind.I4);
        var character = fixture.AddPrimitive("char", CliValueKind.I4);
        var shortType = fixture.AddPrimitive("u2", CliValueKind.I4);
        var integer = fixture.AddPrimitive("i4", CliValueKind.I4);
        var single = fixture.AddPrimitive("f4", CliValueKind.F4);
        var actor = fixture.Actor();
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(byteType), CliTypeIdentity.SzArray(boolean), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(character), CliTypeIdentity.SzArray(shortType), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(integer), CliTypeIdentity.SzArray(single), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(byteType), CliTypeIdentity.SzArray(shortType), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.Array(integer, 1), CliTypeIdentity.SzArray(integer), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.SzArray(integer), CliTypeIdentity.Array(integer, 2), Empty));
        Assert.True(actor.IsAssignable(CliTypeIdentity.SzArray(integer), CliTypeIdentity.Array(integer, 1), Empty));
    }

    [Theory]
    [InlineData("IEnumerable`1", true)]
    [InlineData("ICollection`1", true)]
    [InlineData("IList`1", true)]
    [InlineData("IReadOnlyCollection`1", true)]
    [InlineData("IReadOnlyList`1", true)]
    [InlineData("Unrelated`1", false)]
    public void VectorInterfaceCompatibilityUsesTheDeclaredCoreInterfaceSet(string name, bool expected)
    {
        var fixture = new Fixture();
        var item = fixture.Add("Item");
        var contract = fixture.Add(name, isInterface: true, typeNamespace: "System.Collections.Generic");
        Assert.Equal(expected, fixture.Actor().IsAssignable(CliTypeIdentity.SzArray(item),
            CliTypeIdentity.GenericInstantiation(contract, [item]), Empty));
    }

    [Fact]
    public void VarianceKeepsEqualArgumentsWhileCheckingTheRemainingPositions()
    {
        var fixture = new Fixture();
        var parent = fixture.Add("Parent");
        var child = fixture.Add("Child", parent: parent);
        var contract = fixture.Add("Pair`2", isInterface: true,
            variances: [CliGenericVariance.Invariant, CliGenericVariance.Covariant]);
        var actor = fixture.Actor();
        Assert.True(actor.IsAssignable(CliTypeIdentity.GenericInstantiation(contract, [parent, child]),
            CliTypeIdentity.GenericInstantiation(contract, [parent, parent]), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.GenericInstantiation(contract, [child, child]),
            CliTypeIdentity.GenericInstantiation(contract, [parent, parent]), Empty));
        Assert.False(actor.IsAssignable(CliTypeIdentity.GenericInstantiation(contract, [parent, child]),
            CliTypeIdentity.GenericInstantiation(contract, [parent]), Empty));
    }

    [Fact]
    public void NominalReferenceBoundsExcludeValueTypesAndUnsupportedConstraintShapes()
    {
        var fixture = new Fixture();
        var contract = fixture.Add("Covariant`1", isInterface: true, variances: [CliGenericVariance.Covariant]);
        var variable = CliTypeIdentity.GenericParameter(false, 0);
        var value = fixture.Add("Value", isValueType: true);
        var text = fixture.AddPrimitive("string", CliValueKind.ManagedReference, isValueType: false);
        var nominal = fixture.Add("Nominal`1");
        var from = CliTypeIdentity.GenericInstantiation(contract, [variable]);
        var to = CliTypeIdentity.GenericInstantiation(contract, [fixture.Object]);
        var actor = fixture.Actor();
        UnsafeAccessorGenericConstraints Bound(CliTypeIdentity type) => new([new(GenericParameterAttributes.None, [type])], []);
        Assert.False(actor.IsAssignable(from, to, Bound(value)));
        Assert.False(actor.IsAssignable(from, to, Bound(CliTypeIdentity.UnmanagedPointer(value))));
        Assert.True(actor.IsAssignable(from, to, Bound(text)));
        Assert.True(actor.IsAssignable(from, to, Bound(CliTypeIdentity.GenericInstantiation(nominal, [text]))));
    }

    [Fact]
    public void VarianceRecognizesMultidimensionalArraysButRequiresDeclaredVariableConstraints()
    {
        var fixture = new Fixture();
        var parent = fixture.Add("Parent");
        var child = fixture.Add("Child", parent: parent);
        var contract = fixture.Add("Covariant`1", isInterface: true, variances: [CliGenericVariance.Covariant]);
        var actor = fixture.Actor();
        Assert.True(actor.IsAssignable(
            CliTypeIdentity.GenericInstantiation(contract, [CliTypeIdentity.Array(child, 2)]),
            CliTypeIdentity.GenericInstantiation(contract, [CliTypeIdentity.Array(parent, 2)]), Empty));
        Assert.False(actor.IsAssignable(
            CliTypeIdentity.GenericInstantiation(contract, [CliTypeIdentity.GenericParameter(false, 0)]),
            CliTypeIdentity.GenericInstantiation(contract, [fixture.Object]), Empty));
    }

    private static readonly UnsafeAccessorGenericConstraints Empty = new([], []);

    private sealed class Fixture : ITypeDefinitionResolver, IBaseTypeIdentityResolver, IImplementedInterfaceResolver, ITypeFinder
    {
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _definitions = [];
        private readonly Dictionary<CliTypeIdentity, CliTypeIdentity?> _parents = [];
        private readonly Dictionary<CliTypeIdentity, ImmutableArray<CliTypeIdentity>> _interfaces = [];
        public CliTypeIdentity Object { get; }
        public CliTypeIdentity ValueType { get; }
        public CliTypeIdentity Array { get; }

        public Fixture()
        {
            Object = Add("Object");
            ValueType = Add("ValueType", parent: Object);
            Array = Add("Array", parent: Object);
        }

        public CliTypeIdentity Add(string name, bool isValueType = false, bool isInterface = false,
            CliTypeIdentity? parent = null, ImmutableArray<CliTypeIdentity> interfaces = default,
            ImmutableArray<CliGenericVariance> variances = default, string assembly = "Test", string typeNamespace = "Test")
        {
            var identity = CliTypeIdentity.Named(new(assembly), typeNamespace, name, isValueType);
            _definitions[identity] = new(new(new(assembly), 0x02000001 + _definitions.Count), typeNamespace, name, isValueType, [], [])
            {
                IsInterface = isInterface,
                GenericParameterVariances = variances.IsDefault ? [] : variances,
            };
            _parents[identity] = parent;
            _interfaces[identity] = interfaces.IsDefault ? [] : interfaces;
            return identity;
        }

        public IUnsafeAccessorTypeAssignability Actor() => Assert.IsAssignableFrom<IUnsafeAccessorTypeAssignability>(
            new UnsafeAccessorTypeAssignability(new UnsafeAccessorSignatureComparer(this), this, this, this, this, Object, ValueType));
        public CliTypeIdentity AddPrimitive(string name, CliValueKind stackKind, bool isValueType = true)
        {
            var identity = CliTypeIdentity.Primitive(name, stackKind, isValueType);
            _definitions[identity] = new(new(new("Test"), 0x02000001 + _definitions.Count), "Test", name, isValueType, [], []);
            _parents[identity] = isValueType ? ValueType : Object;
            _interfaces[identity] = [];
            return identity;
        }
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => _definitions[Definition(identity)];
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity identity) => _parents[Definition(identity)];
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity identity) => _interfaces[Definition(identity)];
        public TypeDefinitionModel FindType(string fullName)
        {
            Assert.Equal("System.Array", fullName);
            return _definitions[Array];
        }
        private static CliTypeIdentity Definition(CliTypeIdentity identity) => identity.Shape == CliTypeShape.GenericInstantiation ? identity.ElementType! : identity;
    }
}
