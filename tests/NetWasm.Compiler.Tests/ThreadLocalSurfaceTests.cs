// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ThreadLocalSurfaceTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DisposalOnlyDoesNotRetainValueAccessors(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ThreadLocalRetention", """
            using System.Threading;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    new ThreadLocal<int>().Dispose();
                    return value;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Where(body => body.Method.DeclaringType is
            { Shape: CliTypeShape.GenericInstantiation, ElementType.FullName: "System.Threading.ThreadLocal`1" })
            .Select(body => body.Method.Definition.Name).ToArray();
        Assert.NotEmpty(methods);
        Assert.DoesNotContain("get_Value", methods);
        Assert.DoesNotContain("set_Value", methods);
        Assert.DoesNotContain("get_Values", methods);
        Assert.DoesNotContain("get_IsValueCreated", methods);
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    [Fact]
    public void DeclaresThePortableThreadLocalContract()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("ThreadLocalSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.ThreadLocal`1");
        Assert.NotNull(type);
        Assert.Equal(Accessibility.Public, type.DeclaredAccessibility);
        Assert.False(type.IsSealed);
        Assert.Contains(type.Interfaces, item => item.ToDisplayString() == "System.IDisposable");
        var members = type.GetMembers().Where(member => member.DeclaredAccessibility == Accessibility.Public &&
            member is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } or IPropertySymbol);
        Assert.Equal(ExpectedMembers.Order(), members.Select(member => member.GetDocumentationCommentId()).Order());
        var dispose = Assert.Single(type.GetMembers("Dispose").OfType<IMethodSymbol>(), method => method.Parameters.Length == 1);
        Assert.True(dispose.IsVirtual);
        Assert.Equal(Accessibility.Protected, dispose.DeclaredAccessibility);
        Assert.Equal(SpecialType.System_Boolean, dispose.Parameters[0].Type.SpecialType);
    }

    private static readonly string[] ExpectedMembers =
    [
        "M:System.Threading.ThreadLocal`1.#ctor",
        "M:System.Threading.ThreadLocal`1.#ctor(System.Boolean)",
        "M:System.Threading.ThreadLocal`1.#ctor(System.Func{`0})",
        "M:System.Threading.ThreadLocal`1.#ctor(System.Func{`0},System.Boolean)",
        "M:System.Threading.ThreadLocal`1.Dispose",
        "M:System.Threading.ThreadLocal`1.ToString",
        "P:System.Threading.ThreadLocal`1.Value",
        "P:System.Threading.ThreadLocal`1.IsValueCreated",
        "P:System.Threading.ThreadLocal`1.Values",
    ];
}
