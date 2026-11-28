// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using System.Text;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class NetWasmCompilerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerBuildsAnEntrylessLibraryFromAllDeclaredExports(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ExportLibrary", """
            using System.Runtime.CompilerServices;
            public static class Worker
            {
                private static int counter;
                [ModuleInitializer]
                public static void Initialize() => counter = 40;
                public static int Value() => ++counter;
                public static long Add(long left, long right) => left + right;
            }
            """);
        var trace = Path.Combine(assets.Directory, "library.trace");
        var options = new CompilerOptions(assembly, [assets.CoreLib], "", "",
            [new RequestedExport("value", "Worker", "Value"),
             new RequestedExport("add", "Worker", "Add")], target,
            DiagnosticTracePath: trace, EntryPointKind: CompilerEntryPointKind.Library,
            IntermediateOutputPath: Path.Combine(assets.Directory, "obj"));

        var first = NetWasmCompiler.Compile(options);
        var repeated = NetWasmCompiler.Compile(options);

        Assert.Null(first.Program.EntryPoint);
        var exports = WasmModuleInspection.ReadExportNames(first.ApplicationModule);
        Assert.DoesNotContain("run", exports);
        Assert.Contains("value", exports);
        Assert.Contains("add", exports);
        Assert.Equal(target, first.Layouts.Target.Target);
        Assert.Equal(["add", "value"], first.Program.Exports.Select(export => export.Name).Order());
        Assert.Contains(first.Program.Methods.Values,
            method => method.Method.Definition.Name == "Value");
        Assert.Contains(first.Program.Methods.Values,
            method => method.Method.Definition.Name == "Add");
        Assert.Single(first.Program.ModuleInitializers);
        Assert.Equal(first.ApplicationModule, repeated.ApplicationModule);
        Assert.Contains("entry=none", File.ReadAllText(trace));
    }

    // A rebuild that can reuse state from the intermediate directory emits the
    // module the first build did, including the proofs that CoreLib's array
    // accesses and divisions, as number formatting's by 10, cannot fail.
    [Fact]
    public void ARebuildEmitsTheModuleTheFirstBuildDid()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("CachedProofs", """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new int[input + 8];
                    for (var i = 0; i < values.Length; i++) values[i] = (i * 37) % 11;
                    Array.Sort(values);
                    return string.Join(",", values).Length + (input * 12345).ToString().Length;
                }
            }
            """);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3,
            IntermediateOutputPath: Path.Combine(assets.Directory, "obj"));

        var first = NetWasmCompiler.Compile(options);
        var rebuilt = NetWasmCompiler.Compile(options);

        Assert.Equal(first.ApplicationModule, rebuilt.ApplicationModule);
    }

    [Fact]
    public void CompilerProducesReachableProgramLayoutsAndDeterministicWasm()
    {
        using var assets = TestAssets.Create();
        var options = Options(assets, []);

        var first = NetWasmCompiler.Compile(options);
        var second = NetWasmCompiler.Compile(options);

        byte[] header = [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0];
        Assert.Equal(header, first.ApplicationModule[..8]);
        Assert.Equal(first.ApplicationModule, second.ApplicationModule);
        Assert.Equal(
            ["char_at", "helper", "index_at", "length_of", "run_case"],
            first.Program.Exports.Select(export => export.Name).Order(StringComparer.Ordinal));
        Assert.Contains(
            first.Program.Methods.Values,
            method => method.Method.Definition.Name == "ExportedOnly");
        Assert.DoesNotContain(
            first.Program.Methods.Values,
            method => method.Method.Definition.Name.Contains(
                "Unused",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            first.Program.StringLiterals,
            value => value.Contains("unused-literal", StringComparison.Ordinal));
        Assert.Contains("aΩ😀\0\ud800", first.Program.StringLiterals);
        Assert.Contains("", first.Program.StringLiterals);

        // A type takes a layout when it is not generic, or as the run-time type of a generic definition itself
        // (typeof(List<>)); a closed instantiation's definition is reached without one, its layout being the
        // instantiation's (ReachableObjectLayoutBuilder).
        var arities = GenericArities(assets.Application, assets.Library, assets.CoreLib);
        var typeIds = first.Program.Types
            .Where(type => arities[type] == 0 || first.Program.RuntimeTypeDefinitions.Contains(type))
            .Select(type => first.Layouts.ObjectLayouts[type].TypeId)
            .ToArray();
        Assert.Equal(typeIds.Length, typeIds.Distinct().Count());
        Assert.All(typeIds, typeId => Assert.True(typeId > 0));
        Assert.True(first.Layouts.StaticDataEnd % 4 == 0);
        Assert.True(first.StaticDataEnd >= first.Layouts.StaticDataEnd);

        var textLayout = first.Layouts.StaticData.Strings["aΩ😀\0\ud800"];
        var segment = first.Layouts.DataSegments.Single(
            candidate => candidate.Address == textLayout.Address);
        Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(
            segment.Data.AsSpan()[first.Layouts.StringLengthOffset..]));
        var codeUnits = Enumerable.Range(0, 6)
            .Select(index => BinaryPrimitives.ReadUInt16LittleEndian(
                segment.Data.AsSpan()[
                    (first.Layouts.StringDataOffset + (index * sizeof(char)))..]))
            .ToArray();
        ushort[] expectedCodeUnits = [97, 937, 55357, 56832, 0, 55296];
        Assert.Equal(expectedCodeUnits, codeUnits);
    }

    [Fact]
    public void CompilerDoesNotImposeALinkerStaticDataCeiling()
    {
        using var assets = TestAssets.Create();
        var source = new StringBuilder(
            "namespace LargeStaticData; public static class EntryPoint { " +
            "public static int Run(int input) { var values = new string[] {");
        for (var index = 0; index < 600; index++)
        {
            source.Append('"');
            source.Append(index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
            source.Append('x', 900);
            source.Append("\",");
        }
        source.Append("}; return input + values.Length; } }");
        var assembly = assets.CompileSource("LargeStaticData", source.ToString());

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "LargeStaticData.EntryPoint",
            "Run",
            []));

        Assert.True(result.StaticDataEnd > 1024 * 1024);
        Assert.NotEmpty(result.ApplicationModule);
    }

    [Fact]
    public void CompilerValidatesEntryPointAndExportNames()
    {
        using var assets = TestAssets.Create();
        var invalidEntry = Assert.Throws<CompilerException>(
            () => NetWasmCompiler.Compile(Options(
                assets,
                [],
                "NetWasm.Fixtures.Application.Counter",
                "Value")));
        Assert.Equal(DiagnosticCode.InvalidEntryPoint, invalidEntry.Diagnostic.Code);
        Assert.Contains("static int Method(int)", invalidEntry.Message);

        RequestedExport duplicate = new(
            "same",
            "NetWasm.Fixtures.Application.EntryPoint",
            "IndexAt");
        var duplicateName = Assert.Throws<CompilerException>(
            () => NetWasmCompiler.Compile(Options(
                assets,
                [duplicate, duplicate])));
        Assert.Equal(DiagnosticCode.InvalidCommandLine, duplicateName.Diagnostic.Code);
        Assert.Contains("duplicate requested export name 'same'", duplicateName.Message);
        Assert.Throws<ArgumentNullException>(() => NetWasmCompiler.Compile(null!));
    }

    [Fact]
    public void CompilerProducesMemory64ManagedModule()
    {
        using var assets = TestAssets.Create();
        var result = NetWasmCompiler.Compile(
            Options(assets, []) with { Target = WasmTarget.Wasm64 });

        Assert.Equal(WasmTarget.Wasm64, result.Layouts.Target.Target);
        Assert.Equal(new byte[] { 0, 0x61, 0x73, 0x6d, 1, 0, 0, 0 },
            result.ApplicationModule[..8]);
        Assert.True(result.ApplicationModule.Length > 8);

        var artifactPath = Environment.GetEnvironmentVariable(
            "NETWASM_MEMORY64_ARTIFACT_PATH");
        if (!string.IsNullOrWhiteSpace(artifactPath))
        {
            File.WriteAllBytes(artifactPath, result.ApplicationModule);
        }
    }


    // The layout contract: every reached type that is not generic has an object layout, as does a generic definition
    // that is a run-time type itself (typeof(Dictionary<,>)); a closed instantiation's definition (List<int>'s) is
    // reached without one, its layout being the instantiation's.
    [Fact]
    public void ReachedTypesHaveLayoutsAsTheirGenericityRequires()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("LayoutContract", """
            using System;
            using System.Collections.Generic;
            public sealed class Item { }
            public static class EntryPoint
            {
                public static int Run(int input) =>
                    new List<Item>().GetType().Name.Length + typeof(Dictionary<,>).Name.Length + input;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        var arities = GenericArities(assembly, assets.CoreLib);

        var closedDefinitions = result.Program.Types.Where(type => arities[type] > 0 &&
            !result.Program.RuntimeTypeDefinitions.Contains(type)).ToArray();
        Assert.NotEmpty(closedDefinitions);
        Assert.NotEmpty(result.Program.RuntimeTypeDefinitions);
        Assert.All(result.Program.Types.Where(type => arities[type] == 0).Concat(result.Program.RuntimeTypeDefinitions),
            type => Assert.True(result.Layouts.ObjectLayouts.ContainsKey(type), $"{type} has no object layout"));
    }

    // Each type definition's generic parameter count, by its key, read from the assemblies' metadata.
    private static Dictionary<EntityKey, int> GenericArities(params string[] assemblies)
    {
        var arities = new Dictionary<EntityKey, int>();
        foreach (var path in assemblies)
        {
            using var stream = File.OpenRead(path);
            using var image = new System.Reflection.PortableExecutable.PEReader(stream);
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(image);
            var assembly = new AssemblyIdentity(metadata.GetString(metadata.GetAssemblyDefinition().Name));
            foreach (var handle in metadata.TypeDefinitions)
            {
                arities[new EntityKey(assembly, System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle))] =
                    metadata.GetTypeDefinition(handle).GetGenericParameters().Count;
            }
        }
        return arities;
    }
}
