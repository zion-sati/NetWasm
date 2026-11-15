// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleInitializerResolverTests
{
    [Fact]
    public void ResolvesOnlyTheModuleStaticConstructorForItsAssembly()
    {
        var assembly = new AssemblyIdentity("fixture");
        var moduleKey = new EntityKey(assembly, 1);
        var ordinaryTypeKey = new EntityKey(assembly, 2);
        var moduleInitializer = Method(assembly, moduleKey, 3, ".cctor");
        var ordinaryInitializer = Method(assembly, ordinaryTypeKey, 4, ".cctor");
        var resolver = new ModuleInitializerResolver(
            [Type(moduleKey, "<Module>"), Type(ordinaryTypeKey, "Program")],
            [ordinaryInitializer, moduleInitializer]);

        Assert.True(resolver.TryResolve(assembly, out var resolved));
        Assert.Equal(moduleInitializer, resolved);
        Assert.False(resolver.TryResolve(new AssemblyIdentity("other"), out _));
    }

    [Fact]
    public void RejectsMultipleModuleInitializersForOneAssembly()
    {
        var assembly = new AssemblyIdentity("fixture");
        var firstModule = new EntityKey(assembly, 1);
        var secondModule = new EntityKey(assembly, 2);

        var error = Assert.Throws<CompilerException>(() => new ModuleInitializerResolver(
            [Type(firstModule, "<Module>"), Type(secondModule, "<Module>")],
            [Method(assembly, firstModule, 3, ".cctor"), Method(assembly, secondModule, 4, ".cctor")]));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void ValidatesInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ModuleInitializerResolver(null!, []));
        Assert.Throws<ArgumentNullException>(() => new ModuleInitializerResolver([], null!));
    }

    private static TypeDefinitionModel Type(EntityKey key, string name) => new(
        key,
        string.Empty,
        name,
        false,
        ImmutableArray<EntityKey>.Empty,
        ImmutableArray<EntityKey>.Empty);

    private static MethodDefinitionModel Method(
        AssemblyIdentity assembly,
        EntityKey declaringType,
        int token,
        string name) => new(
            new EntityKey(assembly, token),
            declaringType,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
}
