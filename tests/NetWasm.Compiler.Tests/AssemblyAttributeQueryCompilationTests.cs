// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An assembly's own attributes ([assembly: ...]) read from an assembly the compile knows, Assembly.GetExecutingAssembly()
// or typeof(T).Assembly, through the generic extensions, Attribute's statics and Assembly's own methods, as
// Microsoft.Testing.Platform reads its version and build metadata. The queries are resolved at compile time, as type and
// member queries are; a filter that is not a sealed attribute type keeps the runtime query, which throws.
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
                    try { assembly.GetCustomAttributes<Attribute>().ToArray(); return 8; }
                    catch (PlatformNotSupportedException) { }
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
}
