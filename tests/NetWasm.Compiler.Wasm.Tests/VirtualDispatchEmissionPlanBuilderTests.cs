// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class VirtualDispatchEmissionPlanBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("Dispatch.Tests");
    private static readonly EntityKey DeclaringTypeKey = Key(0x02000001);

    [Fact]
    public void CollapsesInheritedConcreteObjectTargetsIntoOneFallback()
    {
        var declaration = Method(
            Key(0x06000001),
            ObjectType(),
            "GetHashCode");
        var firstInherited = new DispatchTargetModel(
            ReferenceType("First"),
            declaration);
        var secondInherited = new DispatchTargetModel(
            ReferenceType("Second"),
            declaration);
        var overriddenMethod = Method(
            Key(0x06000002),
            ReferenceType("Override"),
            "GetHashCode");
        var overridden = new DispatchTargetModel(
            overriddenMethod.DeclaringType,
            overriddenMethod);

        var plan = new VirtualDispatchEmissionPlanBuilder().Build(
            declaration,
            [firstInherited, overridden, secondInherited]);

        Assert.Equal(firstInherited, plan.FallbackTarget);
        Assert.Same(overridden, Assert.Single(plan.CheckedTargets));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PreservesTargetsWithoutSafeRootObjectFallback(
        bool objectDeclaration,
        bool abstractDeclaration)
    {
        var declaringType = objectDeclaration
            ? ObjectType()
            : ReferenceType("Base");
        var declaration = Method(
            Key(0x06000003),
            declaringType,
            "Virtual",
            abstractDeclaration);
        var target = new DispatchTargetModel(
            ReferenceType("Receiver"),
            declaration);

        var plan = new VirtualDispatchEmissionPlanBuilder().Build(
            declaration,
            [target]);

        Assert.Null(plan.FallbackTarget);
        Assert.Same(target, Assert.Single(plan.CheckedTargets));
    }

    [Fact]
    public void PreservesRootObjectTargetsWhenNoneUseTheDeclaration()
    {
        var declaration = Method(
            Key(0x06000004),
            ObjectType(),
            "GetHashCode");
        var overrideMethod = Method(
            Key(0x06000005),
            ReferenceType("Override"),
            "GetHashCode");
        var target = new DispatchTargetModel(
            overrideMethod.DeclaringType,
            overrideMethod);

        var plan = new VirtualDispatchEmissionPlanBuilder().Build(
            declaration,
            [target]);

        Assert.Null(plan.FallbackTarget);
        Assert.Same(target, Assert.Single(plan.CheckedTargets));
    }

    [Fact]
    public void RejectsNullDeclaration() =>
        Assert.Throws<ArgumentNullException>(() =>
            new VirtualDispatchEmissionPlanBuilder().Build(null!, []));

    private static MethodInstanceModel Method(
        EntityKey key,
        CliTypeIdentity declaringType,
        string name,
        bool isAbstract = false)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            key,
            DeclaringTypeKey,
            name,
            false,
            signature,
            1)
        {
            IsVirtual = true,
            IsAbstract = isAbstract,
        };
        return new(definition, declaringType, [], signature);
    }

    private static CliTypeIdentity ObjectType() =>
        CliTypeIdentity.Named(
            Assembly,
            "System",
            "Object",
            isValueType: false);

    private static CliTypeIdentity ReferenceType(string name) =>
        CliTypeIdentity.Named(
            Assembly,
            "Dispatch",
            name,
            isValueType: false);

    private static EntityKey Key(int token) => new(Assembly, token);
}
