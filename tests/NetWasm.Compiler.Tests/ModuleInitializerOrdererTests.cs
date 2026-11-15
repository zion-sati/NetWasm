// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleInitializerOrdererTests
{
    [Fact]
    public void OrdersReachableModuleInitializersDependencyFirst()
    {
        var application = new AssemblyIdentity("application");
        var library = new AssemblyIdentity("library");
        var dependency = new AssemblyIdentity("dependency");
        var unrelated = new AssemblyIdentity("unrelated");
        var applicationInitializer = new EntityKey(application, 1);
        var libraryInitializer = new EntityKey(library, 2);
        var dependencyInitializer = new EntityKey(dependency, 3);
        var unrelatedInitializer = new EntityKey(unrelated, 4);
        var orderer = new ModuleInitializerOrderer(
            new Dictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>
            {
                [application] = [library],
                [library] = [dependency],
            });

        Assert.Equal(
            new[] { dependencyInitializer, libraryInitializer, applicationInitializer, unrelatedInitializer },
            orderer.Order([
                applicationInitializer,
                unrelatedInitializer,
                dependencyInitializer,
                libraryInitializer,
            ]).ToArray());
    }

    [Fact]
    public void RejectsModuleInitializerDependencyCycles()
    {
        var first = new AssemblyIdentity("first");
        var second = new AssemblyIdentity("second");
        var orderer = new ModuleInitializerOrderer(
            new Dictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>
            {
                [first] = [second],
                [second] = [first],
            });

        var error = Assert.Throws<CompilerException>(() => orderer.Order([
            new EntityKey(first, 1),
            new EntityKey(second, 2),
        ]));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void ValidatesInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ModuleInitializerOrderer(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ModuleInitializerOrderer(
                new Dictionary<AssemblyIdentity, IReadOnlyList<AssemblyIdentity>>()).Order(null!));
    }
}
