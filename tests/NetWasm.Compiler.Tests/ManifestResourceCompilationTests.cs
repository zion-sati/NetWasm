// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An assembly's embedded resources, its name and loading it by name, as .NET answers them for an assembly the
// process has loaded. The compiler places the resources and names in the module only for a program that asks;
// the trimmer's ILLink descriptors are left out, as .NET's trimmer leaves them out.
public sealed class ManifestResourceCompilationTests
{
    private const string Marker = "netwasm-resource-marker-7f3a";

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ResourcesNamesAndLoadingAnswerForTheProgramsAssemblies(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"Resources{target}", """
            public static int Run(int input)
            {
                var assembly = typeof(EntryPoint).Assembly;
                var names = assembly.GetManifestResourceNames();
                if (names.Length != 2 || names[0] != "ResourceFixture.greeting.txt" || names[1] != "!Assets") return 1;
                using (var stream = assembly.GetManifestResourceStream("ResourceFixture.greeting.txt"))
                {
                    var bytes = new byte[8];
                    if (stream == null || stream.Length != 5 || stream.Read(bytes, 0, 8) != 5 || bytes[0] != (byte)'h' ||
                        bytes[4] != (byte)'o' || stream.CanWrite) return 2;
                }
                using (var assets = assembly.GetManifestResourceStream("!Assets"))
                {
                    if (assets == null || assets.Length != MarkerLength) return 3;
                }
                if (assembly.GetManifestResourceStream("missing") != null) return 4;
                var info = assembly.GetManifestResourceInfo("!Assets");
                if (info == null || info.ResourceLocation != (ResourceLocation.Embedded | ResourceLocation.ContainedInManifestFile) ||
                    assembly.GetManifestResourceInfo("missing") != null) return 5;
                if (assembly.GetName().Name != "Resources" + Target) return 6;
                var listed = false;
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies()) listed |= loaded == assembly;
                if (!listed) return 7;
                if (Assembly.Load("Resources" + Target) != assembly ||
                    Assembly.Load(new AssemblyName(("Resources" + Target).ToLowerInvariant())) != assembly ||
                    AppDomain.CurrentDomain.Load("Resources" + Target) != assembly) return 8;
                try { Assembly.Load("Missing.Assembly"); return 9; } catch (FileNotFoundException) { }
                return input + 1;
            }
            """, target);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ResourceFixture.EntryPoint", "Run", [], target));
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
        Assert.True(Contains(result.ApplicationModule, Marker));
        Assert.False(Contains(result.ApplicationModule, "ILLink descriptor"));
    }

    // A program that reads no resources and asks for no assembly names carries neither.
    [Fact]
    public void AProgramThatReadsNoResourcesCarriesNone()
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, "ResourcesUnused", """
            public static int Run(int input) => input + 1;
            """, WasmTarget.Wasm32);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ResourceFixture.EntryPoint", "Run", [], WasmTarget.Wasm32));
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
        Assert.False(Contains(result.ApplicationModule, Marker));
        Assert.False(Contains(result.ApplicationModule, "ResourceFixture.greeting.txt"));
    }

    // A library's FxResources.<Name>.SR string table is kept only when the program reaches the type
    // FxResources.<Name>.SR; a resource named like one that is no string table is kept either way.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStringTableIsKeptOnlyForAProgramThatReachesItsType(bool reached)
    {
        using var assets = TestAssets.Create();
        var table = Path.Combine(assets.Directory, "strings.resources");
        File.WriteAllText(table, "table");
        var lookalike = Path.Combine(assets.Directory, "strings.txt");
        File.WriteAllText(lookalike, "text");
        var assembly = assets.CompileSourceWithResources($"Strings{reached}", $$"""
            namespace FxResources.StringsFixture
            {
                internal static class SR { }
            }

            namespace StringsFixture
            {
                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        var assembly = typeof(EntryPoint).Assembly;
                        {{(reached ? "if (typeof(FxResources.StringsFixture.SR).Assembly != assembly) return 1;" : "")}}
                        var names = assembly.GetManifestResourceNames();
                        var table = System.Array.IndexOf(names, "FxResources.StringsFixture.SR.resources") >= 0;
                        var lookalike = System.Array.IndexOf(names, "FxResources.StringsFixture.SR.txt") >= 0;
                        return names.Length == {{(reached ? 2 : 1)}} && table == {{(reached ? "true" : "false")}} && lookalike
                            ? input + 1 : 2;
                    }
                }
            }
            """, [(table, "FxResources.StringsFixture.SR.resources"), (lookalike, "FxResources.StringsFixture.SR.txt")]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "StringsFixture.EntryPoint", "Run", [], WasmTarget.Wasm32));
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // ResourceManager reads an assembly's embedded .resources, as a resx-generated class (Microsoft.Testing.Platform's
    // among them) asks it to: strings by name, case-insensitive names and keys, missing keys, the resource set, and a
    // base name the assembly does not embed. Its neutral resources are the main assembly's, as upstream finds them in
    // invariant globalization mode. ResourceManager(typeof(T)) names the resources after T, a nested T after its
    // outermost type's namespace, which the compiler supplies; a Type it cannot see throws, as NetWasm keeps no type
    // names at run time.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ResourceManagerReadsEmbeddedResources(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var table = Path.Combine(assets.Directory, "Strings.resources");
        using (var writer = new System.Resources.ResourceWriter(table))
        {
            writer.AddResource("Greeting", "hello");
            writer.AddResource("Farewell", "goodbye {0}");
        }
        var nested = Path.Combine(assets.Directory, "Nested.resources");
        using (var writer = new System.Resources.ResourceWriter(nested))
        {
            writer.AddResource("Greeting", "nested");
        }
        var assembly = assets.CompileSourceWithResources($"ResourceManager{target}", """
            using System;
            using System.Globalization;
            using System.Reflection;
            using System.Resources;
            namespace ResourceManagerFixture
            {
                public sealed class Probe : ResourceManager
                {
                    public static CultureInfo Neutral(Assembly assembly) => GetNeutralResourcesLanguage(assembly);
                }

                public static class Strings
                {
                    public static ResourceManager Manager() => new ResourceManager(typeof(Strings));

                    public static class Nested
                    {
                        public static ResourceManager Manager() => new ResourceManager(typeof(Nested));
                    }
                }

                public static class EntryPoint
                {
                    public static int Run(int input)
                    {
                        var assembly = typeof(EntryPoint).Assembly;
                        var strings = new ResourceManager("ResourceManagerFixture.Strings", assembly);
                        if (strings.BaseName != "ResourceManagerFixture.Strings" || strings.GetString("Greeting") != "hello") return 1;
                        if (string.Format(strings.GetString("Farewell", CultureInfo.InvariantCulture)!, "x") != "goodbye x") return 2;
                        if (strings.GetString("Missing") != null || strings.GetString("greeting") != null) return 3;
                        strings.IgnoreCase = true;
                        if (strings.GetString("greeting") != "hello") return 4;
                        var set = strings.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
                        var count = 0;
                        foreach (System.Collections.DictionaryEntry entry in set!) count++;
                        if (count != 2 || set.GetString("Farewell") != "goodbye {0}") return 5;
                        var differentCase = new ResourceManager("resourcemanagerfixture.strings", assembly);
                        if (differentCase.GetString("Greeting") != "hello") return 6;
                        try { new ResourceManager("ResourceManagerFixture.Missing", assembly).GetString("Greeting"); return 7; }
                        catch (MissingManifestResourceException) { }
                        if (Strings.Manager().GetString("Greeting") != "hello" || Strings.Manager().BaseName != "ResourceManagerFixture.Strings") return 8;
                        if (Strings.Nested.Manager().BaseName != "ResourceManagerFixture.Nested" || Strings.Nested.Manager().GetString("Greeting") != "nested") return 10;
                        try { new ResourceManager(Unseen(input)); return 11; }
                        catch (PlatformNotSupportedException) { }
                        if (Probe.Neutral(assembly) != CultureInfo.InvariantCulture) return 9;
                        return input + 1;
                    }

                    private static Type Unseen(int input) => input > 0 ? typeof(EntryPoint) : typeof(Probe);
                }
            }
            """, [(table, "ResourceManagerFixture.Strings.resources"), (nested, "ResourceManagerFixture.Nested.resources")]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ResourceManagerFixture.EntryPoint", "Run", [new("run", "ResourceManagerFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private static string Compile(TestAssets assets, string name, string run, WasmTarget target)
    {
        var greeting = Path.Combine(assets.Directory, name + ".greeting.txt");
        File.WriteAllText(greeting, "hello");
        var marker = Path.Combine(assets.Directory, name + ".assets.bin");
        File.WriteAllText(marker, Marker);
        var descriptor = Path.Combine(assets.Directory, name + ".ILLink.Descriptors.xml");
        File.WriteAllText(descriptor, "<linker><!-- ILLink descriptor --></linker>");
        return assets.CompileSourceWithResources(name, $$"""
            using System;
            using System.IO;
            using System.Reflection;
            namespace ResourceFixture
            {
                public static class EntryPoint
                {
                    private const int MarkerLength = {{Marker.Length}};
                    private const string Target = "{{(name.StartsWith("ResourcesWasm", StringComparison.Ordinal) ? target.ToString() : "Unused")}}";
                    {{run}}
                }
            }
            """, [(greeting, "ResourceFixture.greeting.txt"), (marker, "!Assets"), (descriptor, "ILLink.Descriptors.xml")]);
    }

    private static bool Contains(byte[] module, string text) =>
        module.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
}
