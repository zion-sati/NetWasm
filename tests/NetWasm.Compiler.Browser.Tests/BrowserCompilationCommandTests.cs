using System.Collections.Immutable;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Browser.Tests;

public sealed class BrowserCompilationCommandTests
{
    [Fact]
    public void CompilesAndProjectsARawFunctionWithoutMetrics()
    {
        var compiler = new RecordingCompiler(Result());
        var projected = BrowserResult();
        var projector = new RecordingProjector(projected);
        var command = new BrowserCompilationCommand(
            compiler, new RecordingImageReader([]), new RecordingEntryPointSelector(), projector);
        var request = Request(BrowserCompilationRequestTests.CreateOptions());

        var actual = command.Compile(request);

        Assert.Same(projected, actual);
        Assert.Equal(request.Options, compiler.Options);
        Assert.Same(compiler.Result, projector.Compilation);
        Assert.Null(projector.Abi);
    }

    [Fact]
    public void CapturesCompilerMetricsAndForwardsTheObserver()
    {
        var report = new CompilerMetricsReport(CompilerMetricsOutcome.Succeeded, null,
            TimeSpan.FromMilliseconds(4), TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(1), []);
        var compiler = new RecordingCompiler(Result(), report);
        var forwarded = new RecordingMetricsObserver();
        var projector = new RecordingProjector(BrowserResult());
        var command = new BrowserCompilationCommand(compiler, new RecordingImageReader([]),
            new RecordingEntryPointSelector(), projector);
        var request = Request(BrowserCompilationRequestTests.CreateOptions() with
        {
            MetricsObserver = forwarded,
        });

        var actual = command.Compile(request);

        Assert.Same(report, actual.CompilerMetrics);
        Assert.NotNull(actual.CompilerTiming);
        Assert.Equal(report.TotalDuration, actual.CompilerTiming.CompilerDuration);
        Assert.Same(report, Assert.Single(forwarded.Reports));
    }

    [Fact]
    public void SelectsAndProjectsTheActualManagedExecutableEntryPoint()
    {
        var entry = Method(0x0600002a);
        var compiler = new RecordingCompiler(Result(entry));
        var selector = new RecordingEntryPointSelector();
        var projector = new RecordingProjector(BrowserResult());
        var command = new BrowserCompilationCommand(compiler, new RecordingImageReader([1, 2, 3]),
            selector, projector);
        var request = Request(BrowserCompilationRequestTests.CreateOptions() with
        {
            EntryPointKind = CompilerEntryPointKind.ManagedExecutable,
        }, selectEntryPoint: true);

        _ = command.Compile(request);

        Assert.Equal([null, 0x0600002a], selector.Tokens);
        Assert.Equal("SelectedType", compiler.Options!.EntryTypeName);
        Assert.Equal("SelectedMethod", compiler.Options.EntryMethodName);
        Assert.Equal(0x06000011, compiler.Options.EntryMethodToken);
        Assert.Equal("ActualType", projector.Options!.EntryTypeName);
        Assert.Equal("ActualMethod", projector.Options.EntryMethodName);
        Assert.Equal(0x0600002a, projector.Options.EntryMethodToken);
        Assert.Equal(ManagedExecutableReturnShape.ExitCode, projector.Abi!.ReturnShape);
    }

    [Fact]
    public void RequiresARequestAndAMetricsReportWhenMetricsWereRequested()
    {
        var command = new BrowserCompilationCommand(new RecordingCompiler(Result()),
            new RecordingImageReader([]), new RecordingEntryPointSelector(),
            new RecordingProjector(BrowserResult()));
        Assert.Throws<ArgumentNullException>(() => command.Compile(null!));
        var request = Request(BrowserCompilationRequestTests.CreateOptions()) with
        {
            CollectCompilerMetrics = true,
        };
        Assert.Throws<InvalidOperationException>(() => command.Compile(request));
    }

    private static BrowserCompilationRequest Request(CompilerOptions options,
        bool selectEntryPoint = false) => new(options,
        new Dictionary<string, byte[]> { [options.EntryAssemblyPath] = [1, 2, 3] },
        new Dictionary<string, string>(), selectEntryPoint);

    private static BrowserCompilationResult BrowserResult() => new([], 0, [], [], null!, null!);

    private static CompilationResult Result(MethodDefinitionModel? entry = null) => new(
        [], Program(entry ?? Method(0x06000001)), null!, null!, 0);

    private static ReachableProgram Program(MethodDefinitionModel entry) => new(entry,
        ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty,
        ImmutableHashSet<EntityKey>.Empty,
        ImmutableHashSet<EntityKey>.Empty,
        [], ImmutableHashSet<string>.Empty, [], ImmutableHashSet<EntityKey>.Empty,
        ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
        ImmutableDictionary<EntityKey, EntityKey>.Empty,
        ImmutableHashSet<ManagedExceptionKind>.Empty);

    private static MethodDefinitionModel Method(int token)
    {
        var assembly = new AssemblyIdentity("Fixture");
        return new(new(assembly, token), new(assembly, 0x02000001), "Main", true,
            MethodSignatureModel.Create(CliValueKind.I4), 1);
    }

    private sealed class RecordingCompiler(
        CompilationResult result,
        CompilerMetricsReport? report = null) : INetWasmCompiler
    {
        public CompilationResult Result { get; } = result;
        public CompilerOptions? Options { get; private set; }

        public CompilationResult Compile(CompilerOptions options)
        {
            Options = options;
            if (report is not null) options.MetricsObserver?.Report(report);
            return Result;
        }
    }

    private sealed class RecordingImageReader(byte[] image) : IManagedAssemblyImageReader
    {
        public byte[] Read(string path) => image;
    }

    private sealed class RecordingEntryPointSelector : IManagedExecutableEntryPointSelector
    {
        public List<int?> Tokens { get; } = [];

        public ManagedExecutableEntryPointSelection SelectEntryPoint(
            string assemblyPath, ReadOnlyMemory<byte> image, int? selectedMethodToken = null)
        {
            Tokens.Add(selectedMethodToken);
            return selectedMethodToken is null
                ? new("SelectedType", "SelectedMethod", 0x06000011,
                    new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void))
                : new("ActualType", "ActualMethod", selectedMethodToken.Value,
                    new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode));
        }
    }

    private sealed class RecordingProjector(BrowserCompilationResult result) :
        IBrowserCompilationResultProjector
    {
        public CompilationResult? Compilation { get; private set; }
        public CompilerOptions? Options { get; private set; }
        public ManagedExecutableEntryPointAbi? Abi { get; private set; }

        public BrowserCompilationResult Project(CompilationResult compilation,
            CompilerOptions options, ManagedExecutableEntryPointAbi? entryPointAbi)
        {
            Compilation = compilation;
            Options = options;
            Abi = entryPointAbi;
            return result;
        }
    }

    private sealed class RecordingMetricsObserver : ICompilerMetricsObserver
    {
        public List<CompilerMetricsReport> Reports { get; } = [];
        public void Report(CompilerMetricsReport report) => Reports.Add(report);
    }
}
