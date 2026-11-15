// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StaticProviderCatalogMultiAssemblyCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void SameAssemblyCatalogConsumerMatchesDesktop(
        bool optimize,
        WasmTarget target)
    {
        var expected = ExecuteDesktop(
            "StaticProviderCatalog.SameAssembly",
            SameAssemblySource,
            "StaticProviderCatalog.Consumer.EntryPoint",
            optimize);
        Assert.Equal(ExpectedResult, expected);

        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor =
            new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StaticProviderCatalog.SameAssembly",
            SameAssemblySource,
            "StaticProviderCatalog.Consumer.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CrossAssemblyCatalogConsumerMatchesDesktop(
        bool optimize,
        WasmTarget target)
    {
        var expected = ExecuteDesktop(
            "StaticProviderCatalog.CrossAssembly",
            ConsumerSource,
            "StaticProviderCatalog.Consumer.EntryPoint",
            optimize,
            ProducerSource);
        Assert.Equal(ExpectedResult, expected);

        using var assets = TestAssets.Create();
        var producer = optimize
            ? assets.CompileOptimizedSource(
                "StaticProviderCatalog.Producer",
                ProducerSource)
            : assets.CompileSource(
                "StaticProviderCatalog.Producer",
                ProducerSource);
        ICompilationScenarioExecutor executor =
            new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StaticProviderCatalog.CrossAssembly",
            ConsumerSource,
            "StaticProviderCatalog.Consumer.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray.Create(producer)));

        Assert.Equal(expected, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void LazyMethodGroupCacheCatalogConsumerMatchesDesktop(
        bool optimize,
        WasmTarget target)
    {
        var expected = ExecuteDesktop(
            "StaticProviderCatalog.LazyMethodGroupCache",
            DelegateCacheConsumerSource,
            "StaticProviderCatalog.Consumer.DelegateCacheEntryPoint",
            optimize,
            DelegateCacheProducerSource);
        Assert.Equal(DelegateCacheExpectedResult, expected);

        using var assets = TestAssets.Create();
        var producer = optimize
            ? assets.CompileOptimizedSource(
                "StaticProviderCatalog.DelegateCacheProducer",
                DelegateCacheProducerSource)
            : assets.CompileSource(
                "StaticProviderCatalog.DelegateCacheProducer",
                DelegateCacheProducerSource);
        ICompilationScenarioExecutor executor =
            new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StaticProviderCatalog.LazyMethodGroupCache",
            DelegateCacheConsumerSource,
            "StaticProviderCatalog.Consumer.DelegateCacheEntryPoint",
            optimize,
            target,
            0,
            ImmutableArray.Create(producer)));

        Assert.Equal(expected, observed);
    }

    private const int ExpectedResult = 8894;

    private const int DelegateCacheExpectedResult = 16008;

    private const string SharedUsings = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using StaticProviderCatalog.Producer;

        """;

    private const string ProducerBody = """
        namespace StaticProviderCatalog.Producer
        {

        public abstract class CatalogCase
        {
            protected CatalogCase(string id, string provider, int content) =>
                (Id, Provider, Content) = (id, provider, content);

            public string Id { get; }

            public string Provider { get; }

            public int Content { get; }
        }

        public sealed class ClosedCase<TValue> : CatalogCase
        {
            public ClosedCase(
                string id,
                string provider,
                int content,
                TValue value) : base(id, provider, content)
            {
                Value = value;
            }

            public ClosedCase(
                string id,
                string provider,
                int content,
                TValue value,
                Func<TValue> creator,
                Func<TValue, int> invoker) : base(id, provider, content)
            {
                Value = value;
                Creator = creator;
                Invoker = invoker;
            }

            public TValue Value { get; }

            public Func<TValue>? Creator { get; }

            public Func<TValue, int>? Invoker { get; }
        }

        public static class ProviderA
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("zA00", "A", 400, 400));
                cases.Add(new ClosedCase<int>("zA01", "A", 401, 401));
                cases.Add(new ClosedCase<int>("zA02", "A", 402, 402));
                cases.Add(new ClosedCase<int>("zA03", "A", 403, 403));
                cases.Add(new ClosedCase<int>("zA04", "A", 404, 404));
                cases.Add(new ClosedCase<int>("zA05", "A", 405, 405));
                cases.Add(new ClosedCase<int>("zA06", "A", 406, 406));
                cases.Add(new ClosedCase<int>("zA07", "A", 407, 407));
                cases.Add(new ClosedCase<int>("zA08", "A", 408, 408));
                cases.Add(new ClosedCase<int>("zA09", "A", 409, 409));
                cases.Add(new ClosedCase<int>("zA10", "A", 410, 410));
                cases.Add(new ClosedCase<int>("zA11", "A", 411, 411));
                return cases.ToArray();
            }
        }

        public static class ProviderB
        {
            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>("aB00", "B", 500, 500));
                cases.Add(new ClosedCase<int>("aB01", "B", 501, 501));
                cases.Add(new ClosedCase<int>("aB02", "B", 502, 502));
                cases.Add(new ClosedCase<int>("aB03", "B", 503, 503));
                cases.Add(new ClosedCase<int>("aB04", "B", 504, 504));
                cases.Add(new ClosedCase<int>("aB05", "B", 505, 505));
                cases.Add(new ClosedCase<int>("aB06", "B", 506, 506));
                cases.Add(new ClosedCase<string>(
                    "aB07",
                    "B",
                    507,
                    "payload",
                    CreateText,
                    MeasureText));
                return cases.ToArray();
            }

            private static string CreateText() => "created";

            private static int MeasureText(string value) => value.Length;
        }

        public static class Catalog
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            public static IReadOnlyList<CatalogCase> Entries => CachedEntries;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(ProviderA.CreateCases());
                entries.AddRange(ProviderB.CreateCases());
                return entries.ToArray();
            }
        }
        }
        """;

    private const string DelegateCacheProducerBody = """
        namespace StaticProviderCatalog.Producer
        {

        public static class DelegateCacheProviderA
        {
            private static readonly string Lifecycle = "A-lifecycle";

            private static readonly int Metadata = 700;

            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<int>(
                    "zA00", Lifecycle, Metadata, Metadata, CreateA00, MeasureA00));
                cases.Add(new ClosedCase<int>(
                    "zA01", Lifecycle, Metadata + 1, Metadata + 1, CreateA01, MeasureA01));
                cases.Add(new ClosedCase<int>(
                    "zA02", Lifecycle, Metadata + 2, Metadata + 2, CreateA02, MeasureA02));
                cases.Add(new ClosedCase<int>(
                    "zA03", Lifecycle, Metadata + 3, Metadata + 3, CreateA03, MeasureA03));
                return cases.ToArray();
            }

            private static int CreateA00() => 7000;

            private static int MeasureA00(int value) => value + 1;

            private static int CreateA01() => 7001;

            private static int MeasureA01(int value) => value + 1;

            private static int CreateA02() => 7002;

            private static int MeasureA02(int value) => value + 1;

            private static int CreateA03() => 7003;

            private static int MeasureA03(int value) => value + 1;
        }

        public static class DelegateCacheProviderB
        {
            private static readonly string Lifecycle = "B-lifecycle";

            private static readonly int Metadata = 800;

            public static CatalogCase[] CreateCases()
            {
                var cases = new List<CatalogCase>();
                cases.Add(new ClosedCase<string>(
                    "aB00", Lifecycle, Metadata, "B00", CreateB00, MeasureB00));
                cases.Add(new ClosedCase<string>(
                    "aB01", Lifecycle, Metadata + 1, "B01", CreateB01, MeasureB01));
                cases.Add(new ClosedCase<string>(
                    "aB02", Lifecycle, Metadata + 2, "B02", CreateB02, MeasureB02));
                cases.Add(new ClosedCase<string>(
                    "aB03", Lifecycle, Metadata + 3, "B03", CreateB03, MeasureB03));
                return cases.ToArray();
            }

            private static string CreateB00() => "created-B00";

            private static int MeasureB00(string value) => value.Length;

            private static string CreateB01() => "created-B01";

            private static int MeasureB01(string value) => value.Length;

            private static string CreateB02() => "created-B02";

            private static int MeasureB02(string value) => value.Length;

            private static string CreateB03() => "created-B03";

            private static int MeasureB03(string value) => value.Length;
        }

        public static class DelegateCacheCatalog
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            public static IReadOnlyList<CatalogCase> Entries => CachedEntries;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(DelegateCacheProviderA.CreateCases());
                entries.AddRange(DelegateCacheProviderB.CreateCases());
                return entries.ToArray();
            }
        }
        }
        """;

    private const string ConsumerBody = """
        namespace StaticProviderCatalog.Consumer
        {

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var source = new List<CatalogCase>(Catalog.Entries);
                var snapshot = source.ToArray();
                var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in snapshot)
                {
                    if (!uniqueIds.Add(entry.Id))
                    {
                        return -1;
                    }
                }

                if (snapshot.Length != 20 || uniqueIds.Count != 20)
                {
                    return -1;
                }

                source.Sort(static (left, right) =>
                    string.CompareOrdinal(left.Id, right.Id));
                IReadOnlyList<CatalogCase> readOnly =
                    new ReadOnlyCollection<CatalogCase>(source);
                var expectedEntries = new[]
                {
                    Catalog.Entries[12],
                    Catalog.Entries[13],
                    Catalog.Entries[14],
                    Catalog.Entries[15],
                    Catalog.Entries[16],
                    Catalog.Entries[17],
                    Catalog.Entries[18],
                    Catalog.Entries[19],
                    Catalog.Entries[0],
                    Catalog.Entries[1],
                    Catalog.Entries[2],
                    Catalog.Entries[3],
                    Catalog.Entries[4],
                    Catalog.Entries[5],
                    Catalog.Entries[6],
                    Catalog.Entries[7],
                    Catalog.Entries[8],
                    Catalog.Entries[9],
                    Catalog.Entries[10],
                    Catalog.Entries[11],
                };
                var expectedIds = new[]
                {
                    "aB00", "aB01", "aB02", "aB03", "aB04", "aB05", "aB06", "aB07",
                    "zA00", "zA01", "zA02", "zA03", "zA04", "zA05", "zA06", "zA07",
                    "zA08", "zA09", "zA10", "zA11",
                };
                var expectedContents = new[]
                {
                    500, 501, 502, 503, 504, 505, 506, 507,
                    400, 401, 402, 403, 404, 405, 406, 407,
                    408, 409, 410, 411,
                };
                if (readOnly.Count != expectedEntries.Length ||
                    expectedEntries.Length != expectedIds.Length ||
                    expectedIds.Length != expectedContents.Length)
                {
                    return -1;
                }

                var index = 0;
                foreach (var entry in readOnly)
                {
                    if (!ReferenceEquals(entry, expectedEntries[index]) ||
                        entry.Id != expectedIds[index] ||
                        entry.Content != expectedContents[index])
                    {
                        return -1;
                    }
                    index++;
                }

                return index == 20 ? 8894 : -1;
            }
        }
        }
        """;

    private const string DelegateCacheConsumerBody = """
        namespace StaticProviderCatalog.Consumer
        {

        public static class DelegateCacheEntryPoint
        {
            public static int Run(int input)
            {
                var source = new List<CatalogCase>(DelegateCacheCatalog.Entries);
                var snapshot = source.ToArray();
                var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in snapshot)
                {
                    if (!uniqueIds.Add(entry.Id))
                    {
                        return -1;
                    }
                }

                if (snapshot.Length != 8 || uniqueIds.Count != 8)
                {
                    return -1;
                }

                source.Sort(static (left, right) =>
                    string.CompareOrdinal(left.Id, right.Id));
                IReadOnlyList<CatalogCase> readOnly =
                    new ReadOnlyCollection<CatalogCase>(source);
                var expectedEntries = new[]
                {
                    DelegateCacheCatalog.Entries[4],
                    DelegateCacheCatalog.Entries[5],
                    DelegateCacheCatalog.Entries[6],
                    DelegateCacheCatalog.Entries[7],
                    DelegateCacheCatalog.Entries[0],
                    DelegateCacheCatalog.Entries[1],
                    DelegateCacheCatalog.Entries[2],
                    DelegateCacheCatalog.Entries[3],
                };
                var expectedIds = new[]
                {
                    "aB00", "aB01", "aB02", "aB03",
                    "zA00", "zA01", "zA02", "zA03",
                };
                var expectedContents = new[]
                {
                    800, 801, 802, 803, 700, 701, 702, 703,
                };
                if (readOnly.Count != expectedEntries.Length ||
                    expectedEntries.Length != expectedIds.Length ||
                    expectedIds.Length != expectedContents.Length)
                {
                    return -1;
                }

                var delegates = new object[16];
                var delegateCount = 0;
                var index = 0;
                foreach (var entry in readOnly)
                {
                    if (!ReferenceEquals(entry, expectedEntries[index]) ||
                        entry.Id != expectedIds[index] ||
                        entry.Content != expectedContents[index])
                    {
                        return -1;
                    }

                    switch (entry)
                    {
                        case ClosedCase<int> intCase
                            when intCase.Creator is not null &&
                                intCase.Invoker is not null:
                            delegates[delegateCount++] = intCase.Creator;
                            delegates[delegateCount++] = intCase.Invoker;
                            break;
                        case ClosedCase<string> stringCase
                            when stringCase.Creator is not null &&
                                stringCase.Invoker is not null:
                            delegates[delegateCount++] = stringCase.Creator;
                            delegates[delegateCount++] = stringCase.Invoker;
                            break;
                        default:
                            return -1;
                    }
                    index++;
                }

                if (index != 8 || delegateCount != 16)
                {
                    return -1;
                }

                for (var outer = 0; outer < delegateCount; outer++)
                {
                    if (delegates[outer] is null)
                    {
                        return -1;
                    }

                    for (var inner = outer + 1; inner < delegateCount; inner++)
                    {
                        if (ReferenceEquals(delegates[outer], delegates[inner]))
                        {
                            return -1;
                        }
                    }
                }

                return index == 8 ? 16008 : -1;
            }
        }
        }
        """;

    private const string ProducerSource = SharedUsings + ProducerBody;

    private const string ConsumerSource = SharedUsings + ConsumerBody;

    private const string DelegateCacheProducerSource =
        SharedUsings + ProducerBody + DelegateCacheProducerBody;

    private const string DelegateCacheConsumerSource =
        SharedUsings + DelegateCacheConsumerBody;

    private const string SameAssemblySource =
        SharedUsings + ProducerBody + ConsumerBody;

    private static int ExecuteDesktop(
        string assemblyName,
        string applicationSource,
        string entryTypeName,
        bool optimize,
        string? dependencySource = null)
    {
        var optimizationLevel = optimize
            ? OptimizationLevel.Release
            : OptimizationLevel.Debug;
        var dependencyBytes = dependencySource is null
            ? null
            : CompileDesktopAssembly(
                assemblyName + ".Producer",
                dependencySource,
                optimizationLevel);
        var applicationBytes = CompileDesktopAssembly(
            assemblyName + ".Application",
            applicationSource,
            optimizationLevel,
            dependencyBytes);

        var loadContext = new AssemblyLoadContext(
            "static-provider-catalog-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        try
        {
            if (dependencyBytes is not null)
            {
                using var dependencyStream = new MemoryStream(
                    dependencyBytes,
                    writable: false);
                _ = loadContext.LoadFromStream(dependencyStream);
            }

            using var applicationStream = new MemoryStream(
                applicationBytes,
                writable: false);
            var applicationAssembly = loadContext.LoadFromStream(applicationStream);
            var entryType = applicationAssembly.GetType(
                entryTypeName,
                throwOnError: true)!;
            var entryMethod = entryType.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static)!;
            return (int)entryMethod.Invoke(null, [0])!;
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static byte[] CompileDesktopAssembly(
        string assemblyName,
        string source,
        OptimizationLevel optimizationLevel,
        byte[]? dependencyBytes = null)
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));
        var references = trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        if (dependencyBytes is not null)
        {
            references.Add(MetadataReference.CreateFromImage(dependencyBytes));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimizationLevel,
                nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success);
        return stream.ToArray();
    }
}
