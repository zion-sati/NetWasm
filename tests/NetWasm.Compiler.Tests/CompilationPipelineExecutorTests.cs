using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationPipelineExecutorTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, 0)]
    [InlineData(WasmTarget.Wasm32, false, 1)]
    [InlineData(WasmTarget.Wasm32, false, 2)]
    [InlineData(WasmTarget.Wasm32, true, 0)]
    [InlineData(WasmTarget.Wasm32, true, 1)]
    [InlineData(WasmTarget.Wasm32, true, 2)]
    [InlineData(WasmTarget.Wasm64, false, 0)]
    [InlineData(WasmTarget.Wasm64, false, 1)]
    [InlineData(WasmTarget.Wasm64, false, 2)]
    [InlineData(WasmTarget.Wasm64, true, 0)]
    [InlineData(WasmTarget.Wasm64, true, 1)]
    [InlineData(WasmTarget.Wasm64, true, 2)]
    public void ExecutePreservesEmissionFactsAcrossBindingAndDisposesTheMetadataLease(
        WasmTarget width, bool metrics, int aliases)
    {
        var fixture = new Fixture(width);
        var executor = Assert.IsAssignableFrom<ICompilationPipelineExecutor>(fixture.Executor);
        var options = fixture.Options(metrics, aliases);

        var result = executor.Execute(options);

        Assert.Same(fixture.Emission.Result.Module, result.ApplicationModule);
        Assert.Same(fixture.Program, result.Program);
        Assert.Same(fixture.Layouts.Snapshot, result.Layouts);
        Assert.Same(fixture.Manifest, result.InteropManifest);
        Assert.Equal(fixture.Emission.Result.StaticDataEnd, result.StaticDataEnd);
        Assert.Equal(fixture.Emission.Result.NativeImports.ToArray(), result.NativeImports.ToArray());
        Assert.Equal(fixture.Emission.Result.FunctionImports.ToArray(), result.FunctionImports.ToArray());
        Assert.Equal(fixture.Emission.Result.RuntimeFeatures.ToArray(), result.RuntimeFeatures.ToArray());
        Assert.Equal(Fixture.SuccessOrder(metrics), fixture.Calls
            .Where(call => call.Capability is not ("progress" or "metric-stage" or "metric-counter"))
            .Select(call => call.Capability));
        var load = fixture.Arguments("metadata-load");
        Assert.Equal(options.EntryAssemblyPath, load[0]);
        Assert.Equal(options.ReferencePaths, Assert.IsType<ImmutableArray<string>>(load[1]));
        Assert.Equal(aliases == 2 ? options.ReferenceAssemblyAliases : null, load[2]);
        Assert.Same(fixture.Metadata, fixture.Arguments("metadata-stage")[1]);
        Assert.Equal(width, fixture.Arguments("layouts")[2]);
        Assert.Same(options, fixture.Arguments("emission-artifact")[0]);
        Assert.Equal(result.ApplicationModule, Assert.IsType<byte[]>(fixture.Arguments("emission-artifact")[1]));
        var emission = fixture.Arguments("emission");
        Assert.Same(fixture.Metadata, emission[0]);
        Assert.Same(fixture.Prepared, emission[1]);
        Assert.Same(fixture.Program, Assert.IsType<CompilationAnalysis>(emission[2]).Program);
        Assert.Same(fixture.Layouts, emission[3]);
        Assert.Same(options, emission[4]);
        var bound = Assert.IsType<CompilationResult>(fixture.Arguments("stack-binding")[0]);
        Assert.NotSame(bound, fixture.StackBoundResult);
        Assert.Same(fixture.StackBoundResult, fixture.Arguments("diagnostic-binding")[0]);
        Assert.NotSame(fixture.StackBoundResult, fixture.DiagnosticBoundResult);
        Assert.Same(fixture.DiagnosticBoundResult, result);
        Assert.Equal(fixture.Emission.Result.StackTraceSymbols,
            Assert.IsType<ImmutableArray<WasmStackTraceSymbol>>(fixture.Arguments("stack-binding")[1]));
        Assert.Equal(metrics ? 1 : 0, fixture.Calls.Count(call => call.Capability == "trace"));
        Assert.Equal(metrics ? 1 : 0, fixture.Calls.Count(call => call.Capability == "metrics-create"));
        if (metrics)
        {
            Assert.Equal(new object?[] { CompilerMetricsOutcome.Succeeded, null }, fixture.Arguments("metric-complete"));
            Assert.Same(options.MetricsObserver, fixture.Arguments("metrics-create")[0]);
            Assert.Contains(fixture.Calls, call => call.Capability == "metric-counter" &&
                Equals(call.Arguments[0], "moduleBytes") && Equals(call.Arguments[1], (long)result.ApplicationModule.Length));
        }
        else
            Assert.DoesNotContain(fixture.Calls, call => call.Capability.StartsWith("metric-", StringComparison.Ordinal));
        Assert.Equal(Enum.GetValues<CompilerProgressStage>(), fixture.Calls.Where(call => call.Capability == "progress")
            .Select(call => Assert.IsType<CompilerProgressStage>(call.Arguments[0])));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ExecuteDisposesTheLeaseAndReportsTheOriginalFailureOutcomeEvenWhenFailureWritingThrows(
        bool metrics, bool canceled, bool writerThrows)
    {
        var fixture = new Fixture(WasmTarget.Wasm32)
        {
            FailureAt = "emission",
            Failure = canceled ? new OperationCanceledException() : new InvalidOperationException("emission failure"),
            FailureWriterThrows = writerThrows,
        };
        var executor = Assert.IsAssignableFrom<ICompilationPipelineExecutor>(fixture.Executor);

        var failure = Record.Exception(() => executor.Execute(fixture.Options(metrics, 0)));

        Assert.Same(writerThrows ? fixture.WriterFailure : fixture.Failure, failure);
        Assert.Single(fixture.Calls, call => call.Capability == "lease-dispose");
        Assert.DoesNotContain(fixture.Calls, call => call.Capability is "success-artifact" or "stack-binding" or "diagnostic-binding");
        var writing = fixture.Arguments("failure-artifact");
        Assert.Equal("emission", writing[1]);
        Assert.Same(fixture.Failure, writing[2]);
        if (metrics)
            Assert.Equal(new object?[] { canceled ? CompilerMetricsOutcome.Canceled : CompilerMetricsOutcome.Failed, "emission" },
                fixture.Arguments("metric-complete"));
        else
            Assert.DoesNotContain(fixture.Calls, call => call.Capability.StartsWith("metric-", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteRejectsNullOptionsBeforeAnyCapabilityRuns()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var executor = Assert.IsAssignableFrom<ICompilationPipelineExecutor>(fixture.Executor);

        Assert.Throws<ArgumentNullException>(() => executor.Execute(null!));

        Assert.Empty(fixture.Calls);
    }

    private sealed class Fixture
    {
        public Fixture(WasmTarget width)
        {
            var assembly = new AssemblyIdentity("PipelineUnitTests");
            var entry = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001),
                "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 0);
            // Passive command input. Builders are substituted, so no CFG or
            // compiler algorithm is needed to construct the program fixture.
            Program = new(entry, [], [], [], [], [], [], [], [], [], []);
            Metadata = new([], Program.EntryPoint!.Key.Assembly, [], [], []);
            Prepared = new(
                ComponentBoundaryContract.Empty,
                new(Program.EntryPoint!, []),
                [],
                ReachabilityRoots.Empty,
                null,
                new WasmOptimizationSettings(
                    FinalWasmOptimization.Oz,
                    WasmInlineAnnotationSelection.SizeAware,
                    WasmInlineAnnotationSelection.SizeAware));
            var target = width == WasmTarget.Wasm32 ? WasmTargetLayout.Wasm32 : WasmTargetLayout.Wasm64;
            var types = new ManagedTypeLayouts([], [], [], [], new(), target, 0, 0, 0, 0);
            var data = new ManagedStaticData(96, [], [], [], [], [], [], [], [], []);
            Layouts = new(new ManagedLayoutSnapshot(types, data));
            Manifest = new(1, width == WasmTarget.Wasm32 ? "wasm32" : "wasm64", new(0, 1, 8),
                new(target.ObjectReferenceSize, 0, 0, 0, 0, 0, 0), [], []);
            Emission = new(new WasmModuleEmissionResult([41, 42], 96, [], [new(1, "entry")])
            {
                NativeImports = [new("library", "entry", [width == WasmTarget.Wasm32 ? WasmValueType.I32 : WasmValueType.I64], WasmValueType.F64)],
                FunctionImports = [new("host", "read", WasmFunctionType.Create(CliValueKind.I4))],
                RuntimeFeatures = [NetWasmRuntimeFeatureIds.LocalTime],
            }, new([], []));
            var lease = Capability<IMetadataCompilationLease>("lease", invoke: (method, _) =>
            {
                Calls.Add((method.Name == "Dispose" ? "lease-dispose" : "lease-snapshot", []));
                return method.Name == "get_Snapshot" ? Metadata : null;
            });
            var metrics = new CompilationMetricsRequest(Capability<ICompilationMetricStageStarter>("metric-stage"),
                Capability<ICompilationMetricCounterRecorder>("metric-counter"), Capability<ICompilationMetricReportCompleter>("metric-complete"));
            Executor = new(
                Capability<IMetadataCompilationLoader>("metadata-load", lease),
                Capability<ICompilationMetadataStage>("metadata-stage"),
                Capability<ICompilationPreparationBuilder>("prepare", Prepared),
                Capability<ICompilationAnalysisBuilder>("analysis", new CompilationAnalysis(Program)),
                Capability<ICompilationLayoutBuilder>("layouts", Layouts),
                Capability<ICompilationRootMapBuilder>("roots", Program),
                Capability<ICompilationEmissionBuilder>("emission", Emission),
                Capability<ICompilationDiagnosticTraceStage>("trace"),
                Capability<ICompilerInitialDiagnosticArtifactWriter>("initial-artifact"),
                Capability<ICompilerEntryDiagnosticArtifactWriter>("entry-artifact"),
                Capability<ICompilerProgramDiagnosticArtifactWriter>("program-artifact"),
                Capability<ICompilerLayoutDiagnosticArtifactWriter>("layout-artifact"),
                Capability<ICompilerRootMapDiagnosticArtifactWriter>("root-artifact"),
                new RecordingEmissionArtifacts((options, bytes) => Calls.Add(("emission-artifact", [options, bytes]))),
                Capability<ICompilerSuccessDiagnosticArtifactWriter>("success-artifact"),
                Capability<ICompilerFailureDiagnosticArtifactWriter>("failure-artifact"),
                Capability<IHostInteropManifestBuilder>("manifest", Manifest),
                Capability<ICompilationComplexityStage>("complexity"),
                Capability<ICompilerProgressReporter>("progress"),
                Capability<ICompilationDiagnosticBindingStage>("diagnostic-binding", invoke: (_, args) =>
                    DiagnosticBoundResult = Assert.IsType<CompilationResult>(args[0]) with { }),
                Capability<ICompilationStackTraceArtifactBinder>("stack-binding", invoke: (_, args) =>
                    StackBoundResult = Assert.IsType<CompilationResult>(args[0]) with { }),
                Capability<ISymbolFormatterFactory>("symbols", ValidationTestData.Symbols()),
                Capability<ICompilationMetricsRequestFactory>("metrics-create", metrics));
        }

        public CompilationPipelineExecutor Executor { get; }
        public ReachableProgram Program { get; }
        public MetadataCompilationSnapshot Metadata { get; }
        public CompilationPreparation Prepared { get; }
        public CompilationLayouts Layouts { get; }
        public CompilationEmission Emission { get; }
        public HostInteropManifest Manifest { get; }
        public CompilationResult? StackBoundResult { get; private set; }
        public CompilationResult? DiagnosticBoundResult { get; private set; }
        public List<(string Capability, object?[] Arguments)> Calls { get; } = [];
        public string? FailureAt { get; init; }
        public Exception Failure { get; init; } = new InvalidOperationException("capability failure");
        public bool FailureWriterThrows { get; init; }
        public InvalidOperationException WriterFailure { get; } = new("failure writer failure");

        public CompilerOptions Options(bool metrics, int aliases) => new("memory/app.dll", ["memory/reference.dll"], "Entry", "Run", [],
            Layouts.Snapshot.Target.Target, DiagnosticTracePath: metrics ? "memory/trace" : null,
            ReferenceAssemblyAliases: aliases switch
            {
                0 => null,
                1 => ImmutableDictionary<string, string>.Empty,
                _ => ImmutableDictionary<string, string>.Empty.Add("Reference", "Canonical"),
            }, MetricsObserver: metrics ? Capability<ICompilerMetricsObserver>("observer") : null);

        public object?[] Arguments(string capability) => Assert.Single(Calls, call => call.Capability == capability).Arguments;

        public static IEnumerable<string> SuccessOrder(bool metrics)
        {
            if (metrics)
                yield return "metrics-create";
            yield return "initial-artifact";
            yield return "metadata-load";
            yield return "lease";
            yield return "lease-snapshot";
            yield return "metadata-stage";
            yield return "symbols";
            yield return "prepare";
            yield return "entry-artifact";
            yield return "analysis";
            yield return "program-artifact";
            yield return "layouts";
            yield return "layout-artifact";
            yield return "roots";
            yield return "root-artifact";
            yield return "emission";
            yield return "complexity";
            yield return "emission-artifact";
            if (metrics)
                yield return "trace";
            yield return "manifest";
            yield return "success-artifact";
            yield return "stack-binding";
            yield return "diagnostic-binding";
            yield return "lease";
            yield return "lease-dispose";
            if (metrics)
                yield return "metric-complete";
        }

        private T Capability<T>(string name, object? result = null, Func<MethodInfo, object?[], object?>? invoke = null) where T : class
        {
            var capability = DispatchProxy.Create<T, RecordingCapability>();
            ((RecordingCapability)(object)capability).InvokeCapability = (method, args) =>
            {
                Calls.Add((name, args));
                if (FailureAt == name)
                    throw Failure;
                if (name == "failure-artifact" && FailureWriterThrows)
                    throw WriterFailure;
                if (invoke is not null)
                    return invoke(method, args);
                if (method.ReturnType != typeof(void) && result is null)
                    throw new InvalidOperationException("unexpected capability result");
                return result;
            };
            return capability;
        }
    }

    // A span cannot be boxed by DispatchProxy. Keep this boundary strongly typed.
    private sealed class RecordingEmissionArtifacts(Action<CompilerOptions, byte[]> record) : ICompilerEmissionDiagnosticArtifactWriter
    {
        public void WriteEmission(CompilerOptions options, ReadOnlySpan<byte> bytes) => record(options, bytes.ToArray());
    }

    // Desktop-only in-memory interface doubles, never part of emitted code.
    public class RecordingCapability : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeCapability { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            InvokeCapability(targetMethod!, args ?? []);
    }
}
