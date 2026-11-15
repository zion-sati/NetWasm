// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MonitorSurfaceTests
{
    [Fact]
    public void DeclaresThePortableMonitorSurface()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("MonitorSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.Monitor");
        Assert.NotNull(type);
        Assert.Equal(Accessibility.Public, type.DeclaredAccessibility);
        Assert.True(type.IsStatic);

        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary)
            .ToArray();
        Assert.All(methods, method =>
        {
            Assert.Equal(Accessibility.Public, method.DeclaredAccessibility);
            Assert.True(method.IsStatic);
        });
        Assert.Equal(2, methods.Count(method => method.Name == "Enter"));
        Assert.Equal(6, methods.Count(method => method.Name == "TryEnter"));
        Assert.Single(methods, method => method.Name == "Exit");
        Assert.Single(methods, method => method.Name == "IsEntered");
        Assert.Equal(5, methods.Count(method => method.Name == "Wait"));
        Assert.Single(methods, method => method.Name == "Pulse");
        Assert.Single(methods, method => method.Name == "PulseAll");

        var byRefBooleanOverloads = methods
            .Where(method => method.Parameters.LastOrDefault()?.RefKind == RefKind.Ref)
            .ToArray();
        Assert.Equal(4, byRefBooleanOverloads.Length);
        Assert.All(byRefBooleanOverloads, method =>
            Assert.Equal(SpecialType.System_Boolean, method.Parameters[^1].Type.SpecialType));

        var exception = compilation.GetTypeByMetadataName("System.Threading.SynchronizationLockException");
        Assert.NotNull(exception);
        Assert.Equal(Accessibility.Public, exception.DeclaredAccessibility);
        Assert.Equal(3, exception.InstanceConstructors.Count(constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public));
    }
}
