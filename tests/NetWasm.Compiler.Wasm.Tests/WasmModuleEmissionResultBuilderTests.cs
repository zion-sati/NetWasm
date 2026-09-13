using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Results;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmModuleEmissionResultBuilderTests
{
    [Fact]
    public void BuildsModuleAndProjectsManagedMethodMetrics()
    {
        var modules = new RecordingModuleBuilder();
        var metrics = new RecordingMetricProjector();
        var builder = CreateBuilder(modules, metrics);
        var import = new WasmFunctionImport("host", "call",
            WasmFunctionType.Create(CliValueKind.Void));
        var function = new WasmFunctionDefinition("run",
            WasmFunctionType.Create(CliValueKind.Void), [1]);
        var export = new WasmExport("run", 1);
        var emission = new ManagedMethodEmissionRecord("run", "key",
            new([1], 2, 3, 4, "key", FilterEnvironmentLayout.Empty,
                ImmutableDictionary<int, int>.Empty));
        var request = new WasmModuleEmissionBuildRequest(
            new([import], "runtime", "memory", [function], [export], [], true,
                WasmTarget.Wasm64, IncludeNameSection: false),
            512,
            [emission],
            [],
            [NetWasmRuntimeFeatureIds.LocalTime]);

        var result = builder.Build(request);

        Assert.Equal([7, 8], result.Module);
        Assert.Equal(512, result.StaticDataEnd);
        Assert.Equal("run", Assert.Single(result.ManagedMethodMetrics).Identity);
        Assert.Equal(WasmTarget.Wasm64, modules.Target);
        Assert.True(modules.IncludeManagedExceptionTag);
        Assert.False(modules.IncludeNameSection);
        Assert.Same(import, Assert.Single(modules.Imports));
        Assert.Same(import, Assert.Single(result.FunctionImports));
        Assert.Same(function, Assert.Single(modules.Functions));
        Assert.Same(export, Assert.Single(modules.Exports));
        Assert.Same(emission, Assert.Single(metrics.Emissions));
        Assert.Equal([NetWasmRuntimeFeatureIds.LocalTime], result.RuntimeFeatures.ToArray());
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateBuilder(new RecordingModuleBuilder(), new RecordingMetricProjector())
                .Build(null!));
    }

    private static IWasmModuleEmissionResultBuilder CreateBuilder(
        IWasmModuleBuilder modules,
        IManagedMethodEmissionMetricProjector metrics) => new[]
    {
        new WasmModuleEmissionResultBuilder(modules, metrics),
    }.Cast<IWasmModuleEmissionResultBuilder>().Single();

    private sealed class RecordingModuleBuilder : IWasmModuleBuilder
    {
        public IReadOnlyList<WasmFunctionImport> Imports { get; private set; } = [];
        public IReadOnlyList<WasmFunctionDefinition> Functions { get; private set; } = [];
        public IReadOnlyList<WasmExport> Exports { get; private set; } = [];
        public bool IncludeManagedExceptionTag { get; private set; }
        public bool IncludeNameSection { get; private set; }
        public WasmTarget Target { get; private set; }

        public byte[] Build(IReadOnlyList<WasmFunctionImport> functionImports,
            string memoryImportModule, string memoryImportName,
            IReadOnlyList<WasmFunctionDefinition> functions,
            IReadOnlyList<WasmExport> exports, IReadOnlyList<DataSegment> dataSegments,
            bool includeManagedExceptionTag = false,
            WasmTarget target = WasmTarget.Wasm32,
            bool includeNameSection = true)
        {
            Imports = functionImports;
            Functions = functions;
            Exports = exports;
            IncludeManagedExceptionTag = includeManagedExceptionTag;
            IncludeNameSection = includeNameSection;
            Target = target;
            return [7, 8];
        }
    }

    private sealed class RecordingMetricProjector :
        IManagedMethodEmissionMetricProjector
    {
        public IReadOnlyList<ManagedMethodEmissionRecord> Emissions { get; private set; } = [];

        public ImmutableArray<WasmManagedMethodEmissionMetric> Project(
            IEnumerable<ManagedMethodEmissionRecord> emissions)
        {
            Emissions = [.. emissions];
            return [new("run", 1, 2, 3, 4,
                ImmutableDictionary<int, int>.Empty)];
        }
    }
}
