// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class FinalizerResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("in-memory");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ResolveFindsDirectOrInheritedFinalizer(int inheritanceDepth)
    {
        var objectType = Type(1, "System", "Object");
        var owner = Type(2, "Fixtures", "Owner");
        var ownerFinalizer = Finalizer(owner.Key, 20);
        owner = owner with { Methods = [ownerFinalizer.Key] };
        var middle = Type(3, "Fixtures", "Middle");
        var derived = Type(4, "Fixtures", "Derived");
        var objectIdentity = Identity(objectType);
        var ownerIdentity = Identity(owner);
        var middleIdentity = Identity(middle);
        var derivedIdentity = Identity(derived);
        var allocated = inheritanceDepth switch
        {
            0 => ownerIdentity,
            1 => middleIdentity,
            _ => derivedIdentity,
        };
        var resolver = Create(
            [(objectIdentity, objectType), (ownerIdentity, owner),
                (middleIdentity, middle), (derivedIdentity, derived)],
            [ownerFinalizer],
            [(ownerIdentity, objectIdentity), (middleIdentity, ownerIdentity),
                (derivedIdentity, middleIdentity)]);

        var resolved = Assert.IsType<MethodInstanceModel>(resolver.Resolve(allocated));

        Assert.Equal(ownerFinalizer, resolved.Definition);
        Assert.Equal(ownerIdentity, resolved.DeclaringType);
    }

    [Fact]
    public void ResolveSelectsNearestOverride()
    {
        var objectType = Type(1, "System", "Object");
        var owner = Type(2, "Fixtures", "Owner");
        var ownerFinalizer = Finalizer(owner.Key, 20);
        owner = owner with { Methods = [ownerFinalizer.Key] };
        var derived = Type(3, "Fixtures", "Derived");
        var derivedFinalizer = Finalizer(derived.Key, 30);
        derived = derived with { Methods = [derivedFinalizer.Key] };
        var objectIdentity = Identity(objectType);
        var ownerIdentity = Identity(owner);
        var derivedIdentity = Identity(derived);
        var resolver = Create(
            [(objectIdentity, objectType), (ownerIdentity, owner), (derivedIdentity, derived)],
            [ownerFinalizer, derivedFinalizer],
            [(ownerIdentity, objectIdentity), (derivedIdentity, ownerIdentity)]);

        var resolved = Assert.IsType<MethodInstanceModel>(resolver.Resolve(derivedIdentity));

        Assert.Equal(derivedFinalizer, resolved.Definition);
        Assert.Equal(derivedIdentity, resolved.DeclaringType);
    }

    [Fact]
    public void ResolveIgnoresSameNameMethodsOutsideTheFinalizerSlot()
    {
        var objectType = Type(1, "System", "Object");
        var owner = Type(2, "Fixtures", "Owner");
        var ownerFinalizer = Finalizer(owner.Key, 20);
        owner = owner with { Methods = [ownerFinalizer.Key] };
        var derived = Type(3, "Fixtures", "Derived");
        var staticMethod = Finalizer(derived.Key, 30) with { IsStatic = true };
        var nonVirtualMethod = Finalizer(derived.Key, 31) with { IsVirtual = false };
        var newSlotMethod = Finalizer(derived.Key, 32) with { IsNewSlot = true };
        var parameterizedMethod = Finalizer(derived.Key, 33) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
        };
        var genericMethod = Finalizer(derived.Key, 34) with { GenericArity = 1 };
        derived = derived with
        {
            Methods = [staticMethod.Key, nonVirtualMethod.Key, newSlotMethod.Key,
                parameterizedMethod.Key, genericMethod.Key],
        };
        var objectIdentity = Identity(objectType);
        var ownerIdentity = Identity(owner);
        var derivedIdentity = Identity(derived);
        var resolver = Create(
            [(objectIdentity, objectType), (ownerIdentity, owner), (derivedIdentity, derived)],
            [ownerFinalizer, staticMethod, nonVirtualMethod, newSlotMethod,
                parameterizedMethod, genericMethod],
            [(ownerIdentity, objectIdentity), (derivedIdentity, ownerIdentity)]);

        var resolved = Assert.IsType<MethodInstanceModel>(resolver.Resolve(derivedIdentity));

        Assert.Equal(ownerFinalizer, resolved.Definition);
    }

    [Fact]
    public void ResolveDoesNotPublishSystemObjectFinalizer()
    {
        var objectType = Type(1, "System", "Object");
        var objectFinalizer = Finalizer(objectType.Key, 10) with { IsNewSlot = true };
        objectType = objectType with { Methods = [objectFinalizer.Key] };
        var ordinary = Type(2, "Fixtures", "Ordinary");
        var objectIdentity = Identity(objectType);
        var ordinaryIdentity = Identity(ordinary);
        var resolver = Create(
            [(objectIdentity, objectType), (ordinaryIdentity, ordinary)],
            [objectFinalizer],
            [(ordinaryIdentity, objectIdentity)]);

        Assert.Null(resolver.Resolve(ordinaryIdentity));
    }

    [Fact]
    public void ResolveReturnsNullWhenTheHierarchyEndsWithoutSystemObject()
    {
        var ordinary = Type(2, "Fixtures", "Ordinary");
        var identity = Identity(ordinary);
        var resolver = Create([(identity, ordinary)], [], []);

        Assert.Null(resolver.Resolve(identity));
    }

    [Fact]
    public void ResolvePreservesClosedGenericBaseIdentity()
    {
        var objectType = Type(1, "System", "Object");
        var genericBase = Type(2, "Fixtures", "Owner`1") with { GenericArity = 1 };
        var finalizer = Finalizer(genericBase.Key, 20);
        genericBase = genericBase with { Methods = [finalizer.Key] };
        var derived = Type(3, "Fixtures", "Derived");
        var objectIdentity = Identity(objectType);
        var genericDefinitionIdentity = Identity(genericBase);
        var closedBaseIdentity = CliTypeIdentity.GenericInstantiation(
            genericDefinitionIdentity,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var derivedIdentity = Identity(derived);
        var resolver = Create(
            [(objectIdentity, objectType), (genericDefinitionIdentity, genericBase),
                (closedBaseIdentity, genericBase), (derivedIdentity, derived)],
            [finalizer],
            [(derivedIdentity, closedBaseIdentity), (closedBaseIdentity, objectIdentity)]);

        var resolved = Assert.IsType<MethodInstanceModel>(resolver.Resolve(derivedIdentity));

        Assert.Equal(closedBaseIdentity, resolved.DeclaringType);
        Assert.Equal(finalizer, resolved.Definition);
    }

    private static FinalizerResolver Create(
        IEnumerable<(CliTypeIdentity Identity, TypeDefinitionModel Definition)> types,
        IEnumerable<MethodDefinitionModel> methods,
        IEnumerable<(CliTypeIdentity Type, CliTypeIdentity BaseType)> baseTypes) =>
        new FinalizerResolver(
            new TypeDefinitions(types),
            new Methods(methods),
            new BaseTypes(baseTypes));

    private static TypeDefinitionModel Type(int token, string @namespace, string name) =>
        new(new EntityKey(Assembly, token), @namespace, name, false, [], []);

    private static CliTypeIdentity Identity(TypeDefinitionModel type) =>
        CliTypeIdentity.Named(Assembly, type.Namespace, type.Name, type.IsValueType);

    private static MethodDefinitionModel Finalizer(EntityKey declaringType, int token) =>
        new(
            new EntityKey(Assembly, token),
            declaringType,
            "Finalize",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            IsVirtual = true,
        };

    private sealed class TypeDefinitions(
        IEnumerable<(CliTypeIdentity Identity, TypeDefinitionModel Definition)> definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(item => item.Identity.CanonicalName, item => item.Definition);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions[identity.CanonicalName];
    }

    private sealed class Methods(IEnumerable<MethodDefinitionModel> methods) : IMethodRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class BaseTypes(
        IEnumerable<(CliTypeIdentity Type, CliTypeIdentity BaseType)> baseTypes) : IBaseTypeResolver
    {
        private readonly Dictionary<string, CliTypeIdentity> _baseTypes =
            baseTypes.ToDictionary(item => item.Type.CanonicalName, item => item.BaseType);

        public CliTypeIdentity? Resolve(CliTypeIdentity type) =>
            _baseTypes.TryGetValue(type.CanonicalName, out var baseType) ? baseType : null;
    }
}
