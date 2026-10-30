using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StaticProviderCatalogAsyncInterfaceCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CrossAssemblyCatalogSurvivesAsyncInterfaceSuspension(
        bool optimize,
        WasmTarget target)
    {
        var expected = ExecuteDesktop(optimize);
        Assert.Equal(ExpectedAsyncResult, expected);

        using var assets = TestAssets.Create();
        var producer = optimize
            ? assets.CompileOptimizedSource(
                "StaticProviderCatalog.AsyncProducer",
                ProducerSource)
            : assets.CompileSource(
                "StaticProviderCatalog.AsyncProducer",
                ProducerSource);
        ICompilationScenarioExecutor executor =
            new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StaticProviderCatalog.AsyncConsumer",
            ConsumerSource,
            "StaticProviderCatalog.AsyncConsumer.EntryPoint",
            optimize,
            target,
            0,
            [producer])
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "StaticProviderCatalog.AsyncConsumer.EntryPoint",
                    "Run"),
                new RequestedExport(
                    "observe",
                    "StaticProviderCatalog.AsyncConsumer.EntryPoint",
                    "Observe"),
            ],
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(expected, observed);
    }

    private const int ImmediateResult = 12020;

    private const int ExpectedAsyncResult = 12021;

    private const string ProducerSource = """
        using System;
        using System.Collections.Generic;

        namespace StaticProviderCatalog.AsyncProducer;

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

        public interface ICatalog
        {
            IEnumerable<CatalogCase> GetEntries();
        }

        public sealed class Catalog : ICatalog
        {
            private readonly IReadOnlyList<CatalogCase> _entries;

            public Catalog(IReadOnlyList<CatalogCase> entries) => _entries = entries;

            public IEnumerable<CatalogCase> GetEntries() => _entries;
        }

        public static class CatalogRoot
        {
            private static readonly CatalogCase[] CachedEntries = CreateEntries();

            private static readonly ICatalog CachedRoot = new Catalog(CachedEntries);

            public static ICatalog Root => CachedRoot;

            private static CatalogCase[] CreateEntries()
            {
                var entries = new List<CatalogCase>();
                entries.AddRange(ProviderA.CreateCases());
                entries.AddRange(ProviderB.CreateCases());
                return entries.ToArray();
            }
        }
        """;

    private const string ConsumerSource = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Threading;
        using System.Threading.Tasks;
        using StaticProviderCatalog.AsyncProducer;

        namespace StaticProviderCatalog.AsyncConsumer;

        public static class EntryPoint
        {
            private static Task<int>? _pending;

            private static int _immediateResult;

            public static int Run(int input)
            {
                _immediateResult = VerifyCatalog(CatalogRoot.Root, 12020);
                _pending = RunAsync(CatalogRoot.Root);
                return _immediateResult;
            }

            public static int Observe(int input)
            {
                if (_pending is null || !_pending.IsCompleted)
                {
                    return -2;
                }

                return _immediateResult == 12020
                    ? _pending.GetAwaiter().GetResult()
                    : -1;
            }

            private static async Task<int> RunAsync(ICatalog catalog)
            {
                var preservedCatalog = catalog;
                await Task.Yield();
                var result = VerifyCatalog(preservedCatalog, 12021);
                return result == 12021 && _immediateResult == 12020
                    ? 12021
                    : -1;
            }

            private static int VerifyCatalog(ICatalog catalog, int result)
            {
                var expectedEntries = new List<CatalogCase>(catalog.GetEntries()).ToArray();
                var source = new List<CatalogCase>(catalog.GetEntries());
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
                    var expectedEntry = expectedEntries[index < 8 ? index + 12 : index - 8];
                    if (!ReferenceEquals(entry, expectedEntry) ||
                        entry.Id != expectedIds[index] ||
                        entry.Content != expectedContents[index])
                    {
                        return -1;
                    }
                    index++;
                }

                return index == 20 ? result : -1;
            }
        }
        """;

    private static int ExecuteDesktop(bool optimize)
    {
        var optimizationLevel = optimize
            ? OptimizationLevel.Release
            : OptimizationLevel.Debug;
        var dependencyBytes = CompileDesktopAssembly(
            "StaticProviderCatalog.AsyncProducer",
            ProducerSource,
            optimizationLevel);
        var applicationBytes = CompileDesktopAssembly(
            "StaticProviderCatalog.AsyncConsumer",
            ConsumerSource,
            optimizationLevel,
            dependencyBytes);
        var loadContext = new AssemblyLoadContext(
            "static-provider-catalog-async-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        try
        {
            using var dependencyStream = new MemoryStream(
                dependencyBytes,
                writable: false);
            _ = loadContext.LoadFromStream(dependencyStream);
            using var applicationStream = new MemoryStream(
                applicationBytes,
                writable: false);
            var applicationAssembly = loadContext.LoadFromStream(applicationStream);
            var entryType = applicationAssembly.GetType(
                "StaticProviderCatalog.AsyncConsumer.EntryPoint",
                throwOnError: true)!;
            var runMethod = entryType.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Static)!;
            var observeMethod = entryType.GetMethod(
                "Observe",
                BindingFlags.Public | BindingFlags.Static)!;
            var immediate = (int)runMethod.Invoke(null, [0])!;
            Assert.Equal(ImmediateResult, immediate);
            var awaited = -2;
            for (var attempt = 0; attempt < 100 && awaited == -2; attempt++)
            {
                Thread.Sleep(1);
                awaited = (int)observeMethod.Invoke(null, [0])!;
            }
            return awaited;
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
