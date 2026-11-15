// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskSchedulerSurfaceTests
{
    [Fact]
    public void DeclaresSchedulerSelectionAndCustomSchedulerContracts()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("TaskSchedulerSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskScheduler");
        Assert.NotNull(type);
        Assert.Equal(Accessibility.Public, type.DeclaredAccessibility);
        Assert.True(type.IsAbstract);
        foreach (var name in new[] { "Current", "Default", "Id", "MaximumConcurrencyLevel" })
            Assert.Equal(Accessibility.Public, Assert.Single(type.GetMembers(name)).DeclaredAccessibility);
        Assert.Equal(Accessibility.Public,
            Assert.Single(type.GetMembers("UnobservedTaskException")).DeclaredAccessibility);
        Assert.True(Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(type.GetMembers("QueueTask"))).IsAbstract);
        Assert.True(Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(type.GetMembers("TryExecuteTaskInline"))).IsAbstract);
        Assert.True(Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(type.GetMembers("GetScheduledTasks"))).IsAbstract);
        Assert.Equal(Accessibility.Protected, Assert.Single(type.GetMembers("TryExecuteTask")).DeclaredAccessibility);
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        Assert.NotNull(task);
        foreach (var name in new[] { "Start", "RunSynchronously" })
            Assert.Contains(task.GetMembers(name).OfType<IMethodSymbol>(), method =>
                method.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, type));
    }
}
