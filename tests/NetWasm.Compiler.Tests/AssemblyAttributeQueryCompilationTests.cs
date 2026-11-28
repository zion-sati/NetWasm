// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An assembly's own attributes ([assembly: ...]) read from an assembly the compile knows, Assembly.GetExecutingAssembly()
// or typeof(T).Assembly, through the generic extensions, Attribute's statics and Assembly's own methods, as
// Microsoft.Testing.Platform reads its version and build metadata. The queries are resolved at compile time, as type and
// member queries are, for any filter: Attribute itself takes every attribute the assembly declares.
public sealed class AssemblyAttributeQueryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AKnownAssemblysAttributesAreRead(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"AssemblyAttributes{target}", """
            using System;
            using System.Linq;
            using System.Reflection;

            [assembly: AssemblyInformationalVersion("1.2.3+abcdef")]
            [assembly: AssemblyMetadata("Key", "Value")]
            [assembly: AssemblyMetadata("Other", "Thing")]

            namespace AssemblyAttributes;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    if (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is not { InformationalVersion: "1.2.3+abcdef" }) return 1;
                    var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
                    if (metadata.Length != 2 || metadata.All(item => item.Key != "Key" || item.Value != "Value") ||
                        metadata.All(item => item.Key != "Other")) return 2;
                    if (typeof(EntryPoint).Assembly.GetCustomAttribute<AssemblyTitleAttribute>() is not null) return 3;
                    if (!Attribute.IsDefined(typeof(EntryPoint).Assembly, typeof(AssemblyInformationalVersionAttribute))) return 4;
                    try { Attribute.GetCustomAttribute(assembly, typeof(AssemblyMetadataAttribute)); return 5; }
                    catch (AmbiguousMatchException) { }
                    if (assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false).Length != 2) return 6;
                    if (!assembly.IsDefined(typeof(AssemblyInformationalVersionAttribute), false)) return 7;
                    if (assembly.GetCustomAttributes<Attribute>().OfType<AssemblyMetadataAttribute>().Count() != 2) return 8;
                    return input + 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AssemblyAttributes.EntryPoint", "Run",
            [new("run", "AssemblyAttributes.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // Attribute's statics that take an inheritance choice answer for an assembly as the ones without it (an assembly
    // has nothing to inherit from), and a filter that is not an attribute type throws ArgumentException, as desktop's
    // do: upstream's bodies validate, then ask the assembly, and return. Bodies that only threw made each call look
    // like one that never returns, so the code after it was dropped before the query was resolved and `throw null` ran.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TheInheritanceOverloadsAnswerForAnAssembly(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"AssemblyInheritOverloads{target}", """
            using System;
            using System.Reflection;

            [assembly: AssemblyInheritOverloads.Leaf(2)]

            namespace AssemblyInheritOverloads;

            public interface IMarker { }

            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            public sealed class LeafAttribute(int id) : Attribute { public int Id { get; } = id; }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var assembly = typeof(EntryPoint).Assembly;
                    if (Attribute.GetCustomAttributes(assembly, typeof(LeafAttribute), true) is not [LeafAttribute { Id: 2 }]) return 1;
                    if (Attribute.GetCustomAttributes(assembly, typeof(LeafAttribute), false) is not [LeafAttribute { Id: 2 }]) return 2;
                    if (!Attribute.IsDefined(assembly, typeof(LeafAttribute), false)) return 3;
                    if (Attribute.GetCustomAttribute(assembly, typeof(LeafAttribute), true) is not LeafAttribute { Id: 2 }) return 4;
                    try { Attribute.IsDefined(assembly, typeof(IMarker), true); return 5; }
                    catch (ArgumentException) { }
                    try { Attribute.GetCustomAttributes(Assembly.GetExecutingAssembly(), typeof(IMarker)); return 6; }
                    catch (ArgumentException) { }
                    return input + 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AssemblyInheritOverloads.EntryPoint", "Run",
            [new("run", "AssemblyInheritOverloads.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
