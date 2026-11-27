// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// With type names, a Type's FullName, Name, Namespace, ToString and AssemblyQualifiedName are desktop .NET 11's, for
// types in the global namespace and nested in them, and for generic instantiations of either, beside a namespaced
// type. The core library's assembly name is NetWasm.CoreLib's, not System.Private.CoreLib's, so both sides write it
// as {corelib}.
public sealed class TypeNameFidelityCompilationTests
{
    private const string AssemblyName = "TypeNameFidelity";

    internal const string Source = """
        using System;
        using System.Collections.Generic;

        public class GlobalType
        {
            public class Nested { public class Deeper { } }
            public class GenericNested<T> { }
        }
        public class GlobalGeneric<T> { public class Inner { } }
        public struct GlobalValue { }

        namespace Sample.Names
        {
            public class Named { public class Nested { } }
        }

        public static class EntryPoint
        {
            public static string Names()
            {
                Type[] types =
                [
                    typeof(GlobalType), typeof(GlobalType.Nested), typeof(GlobalType.Nested.Deeper),
                    typeof(GlobalType.GenericNested<int>), typeof(GlobalType.GenericNested<GlobalType>),
                    typeof(GlobalGeneric<int>), typeof(GlobalGeneric<GlobalType>), typeof(GlobalGeneric<GlobalType.Nested>),
                    typeof(GlobalGeneric<int>.Inner), typeof(GlobalValue), typeof(List<GlobalType>),
                    typeof(Dictionary<GlobalValue, GlobalGeneric<int>>), typeof(Sample.Names.Named),
                    typeof(Sample.Names.Named.Nested), typeof(GlobalGeneric<Sample.Names.Named>), typeof(GlobalType[]),
                    new GlobalType.Nested().GetType(), new GlobalGeneric<string>().GetType(),
                ];
                var corelib = typeof(int).Assembly.FullName!;
                var lines = new List<string>();
                foreach (var type in types)
                {
                    lines.Add(string.Join(" | ", type.FullName, type.Name, type.Namespace ?? "<null>", type.ToString(),
                        type.AssemblyQualifiedName).Replace(corelib, "{corelib}"));
                }
                return string.Join("\n", lines);
            }

            public static int Run(int input)
            {
                Console.WriteLine(Names());
                return input;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void TypeNamesAreDesktops(FinalWasmOptimization optimization)
    {
        var expected = DesktopNames();
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(AssemblyName, Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [new RequestedExport("run", "EntryPoint", "Run")],
            WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EmitTypeNames: true,
            ProjectDirectory: assets.Directory,
            Optimization: optimization));

        var output = CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(compilation.ApplicationModule,
            assets.Directory, 0, WasmTarget.Wasm32, compilation.StaticDataEnd, compilation.TypeSymbols!.Bytes);

        Assert.Equal(expected.Split('\n'), output.TrimEnd('\n').Split('\n'));
    }

    private static string DesktopNames()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(AssemblyName, [CSharpSyntaxTree.ParseText(Source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream).Success);
        stream.Position = 0;
        var context = new AssemblyLoadContext(AssemblyName, isCollectible: true);
        try
        {
            return (string)context.LoadFromStream(stream).GetType("EntryPoint")!.GetMethod("Names")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
