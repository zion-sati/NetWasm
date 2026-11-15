// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedMethodIdentityFactoryTests
{
    private readonly ManagedMethodIdentityFactory _factory = new();

    [Fact]
    public void CreateUsesTheSameCanonicalIdentityForAnInstanceAndItsDefinition()
    {
        CliTypeIdentity declaringType = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "Tests",
            "Container",
            isValueType: false);
        MethodDefinitionModel definition = CreateDefinition();
        var instance = new MethodInstanceModel(
            definition,
            declaringType,
            ImmutableArray<CliTypeIdentity>.Empty,
            definition.Signature);
        IManagedMethodIdentityFactory factory =
            Assert.IsAssignableFrom<IManagedMethodIdentityFactory>(_factory);

        ManagedMethodIdentity fromInstance = factory.Create(instance);
        ManagedMethodIdentity fromDefinition = factory.Create(definition, declaringType);

        Assert.Equal(instance.CanonicalName, fromInstance.CanonicalName);
        Assert.Equal(fromInstance, fromDefinition);
    }

    [Fact]
    public void CreateRejectsMissingMethodFacts()
    {
        MethodDefinitionModel definition = CreateDefinition();
        CliTypeIdentity declaringType = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "Tests",
            "Container",
            isValueType: false);
        IManagedMethodIdentityFactory factory =
            Assert.IsAssignableFrom<IManagedMethodIdentityFactory>(_factory);

        Assert.Throws<ArgumentNullException>(() => factory.Create((MethodInstanceModel)null!));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, declaringType));
        Assert.Throws<ArgumentNullException>(() => factory.Create(definition, null!));
    }

    private static MethodDefinitionModel CreateDefinition()
    {
        var assembly = new AssemblyIdentity("Tests");
        return new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Run",
            IsStatic: true,
            MethodSignatureModel.Create(CliValueKind.Void),
            RelativeVirtualAddress: 0);
    }
}
