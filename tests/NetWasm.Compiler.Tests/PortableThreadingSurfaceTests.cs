// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PortableThreadingSurfaceTests
{
    [Fact]
    public void DeclaresPublicStandardVolatileAndTimeoutContracts()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("PortableThreadingSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Threading.Volatile");
        Assert.NotNull(type);
        Assert.Equal(Accessibility.Public, type.DeclaredAccessibility);
        Assert.True(type.IsStatic);
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility == Accessibility.Public).ToArray();
        Assert.Equal(ExpectedMembers.Order(), methods.Select(method => method.GetDocumentationCommentId()).Order());
        Assert.All(methods, method =>
        {
            Assert.True(method.IsStatic);
            Assert.Equal(RefKind.Ref, method.Parameters[0].RefKind);
            if (method.Name == "Read")
                Assert.True(SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[0].Type));
            else
            {
                Assert.True(method.ReturnsVoid);
                Assert.True(SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.Parameters[1].Type));
            }
            if (method.IsGenericMethod)
                Assert.True(Assert.Single(method.TypeParameters).HasReferenceTypeConstraint);
        });

        var timeout = compilation.GetTypeByMetadataName("System.Threading.Timeout");
        Assert.NotNull(timeout);
        Assert.Equal(Accessibility.Public, timeout.DeclaredAccessibility);
        Assert.True(timeout.IsStatic);
        var infinite = Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(timeout.GetMembers("Infinite")));
        Assert.Equal(Accessibility.Public, infinite.DeclaredAccessibility);
        Assert.True(infinite.IsConst);
        Assert.Equal(-1, infinite.ConstantValue);
        var timespan = Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(timeout.GetMembers("InfiniteTimeSpan")));
        Assert.Equal(Accessibility.Public, timespan.DeclaredAccessibility);
        Assert.True(timespan.IsStatic);
        Assert.True(timespan.IsReadOnly);
        Assert.Equal("System.TimeSpan", timespan.Type.ToDisplayString());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ReadRetainsOnlyItsOwnOperation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("VolatileRetention", """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input != 0) return Volatile.Read(ref input);
                    try { return Volatile.Read(ref Unsafe.NullRef<int>()); }
                    catch (NullReferenceException) { return -1; }
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [])
        {
            Target = target,
        });
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .Where(method => method.DeclaringType.FullName == "System.Threading.Volatile").ToArray();
        // Read and its location check are small and may be inlined, as upstream's AggressiveInlining asks; the check
        // folds away where the address cannot be null (an argument's). Nothing else of threading comes with them, and a
        // null location still throws as on desktop.
        Assert.Subset(new HashSet<string> { "Read", "ValidateLocation" },
            methods.Select(method => method.Definition.Name).ToHashSet());
        Assert.DoesNotContain(result.Program.Methods.Values,
            body => body.Method.DeclaringType.FullName == "System.Threading.Timeout");
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 42, target, result.StaticDataEnd));
        Assert.Equal(-1, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    // The .NET Standard 2.1 scalar and reference surface. This does not imply
    // shared-memory threading or the newer desktop ReadBarrier/WriteBarrier APIs.
    private static readonly string[] ExpectedMembers =
    [
        "M:System.Threading.Volatile.Read(System.Boolean@)",
        "M:System.Threading.Volatile.Write(System.Boolean@,System.Boolean)",
        "M:System.Threading.Volatile.Read(System.Byte@)",
        "M:System.Threading.Volatile.Write(System.Byte@,System.Byte)",
        "M:System.Threading.Volatile.Read(System.SByte@)",
        "M:System.Threading.Volatile.Write(System.SByte@,System.SByte)",
        "M:System.Threading.Volatile.Read(System.Int16@)",
        "M:System.Threading.Volatile.Write(System.Int16@,System.Int16)",
        "M:System.Threading.Volatile.Read(System.UInt16@)",
        "M:System.Threading.Volatile.Write(System.UInt16@,System.UInt16)",
        "M:System.Threading.Volatile.Read(System.Int32@)",
        "M:System.Threading.Volatile.Write(System.Int32@,System.Int32)",
        "M:System.Threading.Volatile.Read(System.UInt32@)",
        "M:System.Threading.Volatile.Write(System.UInt32@,System.UInt32)",
        "M:System.Threading.Volatile.Read(System.Int64@)",
        "M:System.Threading.Volatile.Write(System.Int64@,System.Int64)",
        "M:System.Threading.Volatile.Read(System.UInt64@)",
        "M:System.Threading.Volatile.Write(System.UInt64@,System.UInt64)",
        "M:System.Threading.Volatile.Read(System.IntPtr@)",
        "M:System.Threading.Volatile.Write(System.IntPtr@,System.IntPtr)",
        "M:System.Threading.Volatile.Read(System.UIntPtr@)",
        "M:System.Threading.Volatile.Write(System.UIntPtr@,System.UIntPtr)",
        "M:System.Threading.Volatile.Read(System.Single@)",
        "M:System.Threading.Volatile.Write(System.Single@,System.Single)",
        "M:System.Threading.Volatile.Read(System.Double@)",
        "M:System.Threading.Volatile.Write(System.Double@,System.Double)",
        "M:System.Threading.Volatile.Read``1(``0@)",
        "M:System.Threading.Volatile.Write``1(``0@,``0)",
    ];
}
