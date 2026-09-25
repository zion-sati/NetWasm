using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PropertyRunnerIsolationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CfgCannotDiscardWasm64MismatchOrMissingExecution(bool skip)
    {
        var compiler = new Compiler();
        var desktop = new Desktop();
        var netWasm = new WrongWasm64(skip);
        var failures = new Failures();
        var compiled = new CompiledCorpusComparisonRunner(
            netWasm, CompiledCorpusTestFactory.RejectingLinked, new CompiledCorpusTestFactory.RecordingReceiptDestinations(),
            new CompiledCorpusTestFactory.RecordingLinkedReceipts(), new OracleComparer(), failures,
            new RandomCilCaseProgressReporter(TextWriter.Null), new CorpusExpectationVerifier(),
            new CorpusExecutionVerifier(), new CorpusCompilationCellsSelector(new CorpusMatrixExpander()));
        var runner = Assert.IsAssignableFrom<ICfgPropertyRunner>(new CfgPropertyRunner(
            new Directories(), compiler, desktop, new OracleComparer(), compiled, new RecordingCorpusCleanup()));

        var failure = Record.Exception(() => runner.Run(Property()));

        Assert.Single(compiler.Calls);
        Assert.Equal(1, desktop.Calls);
        Assert.Equal([WasmTarget.Wasm32, WasmTarget.Wasm64], netWasm.Targets);
        if (skip)
        {
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Contains("required Wasm64 execution was skipped", failure.Message, StringComparison.Ordinal);
            Assert.Empty(failures.Targets);
        }
        else
        {
            Assert.IsType<CorpusOracleMismatchException>(failure);
            Assert.Contains("Wasm64", failure.Message, StringComparison.Ordinal);
            Assert.Equal(WasmTarget.Wasm64, Assert.Single(failures.Targets));
        }
    }

    [Fact]
    public void CfgRunsUseDistinctRootsAndDelegateBothProfilesAgainstTheInterpreter()
    {
        var directories = new Directories();
        var compiler = new Compiler();
        var desktop = new Desktop();
        var compiled = new Compiled();
        var cleanup = new RecordingCorpusCleanup
        {
            BeforeClean = _ => Assert.Equal(directories.Calls * 2, compiled.Comparisons.Count),
        };
        var runner = Assert.IsAssignableFrom<ICfgPropertyRunner>(new CfgPropertyRunner(directories, compiler, desktop, new Comparer(), compiled, cleanup));
        var property = Property();

        runner.Run(property);
        runner.Run(property);

        AssertPaths(compiler.Calls, directories);
        Assert.Equal(["run-1", "run-2"], cleanup.Directories);
        Assert.Equal(4, desktop.Calls);
        Assert.Equal(4, compiled.Comparisons.Count);
        Assert.All(compiled.Comparisons, call =>
        {
            Assert.Same(property.Expected, call.Expected);
            Assert.True(call.Compilation.Fixture.ExecuteWasm64);
            Assert.Equal(property.Fixture.Source, call.Compilation.Fixture.Source);
        });
        Assert.Equal(compiler.Calls, compiled.Comparisons.Select(call => call.Compilation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CfgStopsAtFirstInterpreterOrSharedComparisonFailure(bool comparisonFailure)
    {
        var compiler = new Compiler();
        var desktop = new Desktop { Wrong = !comparisonFailure };
        var cause = new InvalidOperationException("shared boundary failure");
        var compiled = new Compiled { Failure = comparisonFailure ? cause : null };
        var cleanup = new RecordingCorpusCleanup();
        var runner = Assert.IsAssignableFrom<ICfgPropertyRunner>(new CfgPropertyRunner(new Directories(), compiler, desktop, new Comparer(), compiled, cleanup));

        var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(Property()));

        Assert.Empty(cleanup.Directories);
        Assert.Equal("run-1", failure.Data["CorpusRunDirectory"]);
        Assert.Single(compiler.Calls);
        Assert.Equal(1, desktop.Calls);
        if (comparisonFailure)
        {
            Assert.Same(cause, failure);
            Assert.Single(compiled.Comparisons);
        }
        else
        {
            Assert.Contains("reference interpreter versus desktop", failure.Message, StringComparison.Ordinal);
            Assert.Empty(compiled.Comparisons);
        }
    }

    [Fact]
    public void NullCfgCaseDoesNotAllocateAWorkDirectory()
    {
        var directories = new Directories();
        var runner = Assert.IsAssignableFrom<ICfgPropertyRunner>(new CfgPropertyRunner(directories, new Compiler(), new Desktop(), new Comparer(), new Compiled(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentNullException>(() => runner.Run(null!));
        Assert.Equal(0, directories.Calls);
    }

    [Fact]
    public void GeneratedRegressionsKeepExactPatchedInputAndUseDistinctRoots()
    {
        var directories = new Directories();
        var compiler = new Compiler();
        var patcher = new Patcher();
        var compiled = new Compiled();
        var policy = new Policy();
        var cleanup = new RecordingCorpusCleanup
        {
            BeforeClean = _ => Assert.Equal(directories.Calls * 2, compiled.Runs.Count),
        };
        var runner = Assert.IsAssignableFrom<IGeneratedCilRegressionRunner>(new GeneratedCilRegressionRunner(directories, compiler, patcher, compiled, policy, cleanup));

        runner.RunRaw(42, [1, 2], [7, 9]);
        runner.RunRaw(42, [1, 2], [7, 9]);

        AssertPaths(compiler.Calls, directories);
        Assert.Equal(4, patcher.Calls.Count);
        Assert.Equal(["run-1", "run-2"], cleanup.Directories);
        Assert.Equal(4, compiled.Runs.Count);
        Assert.Equal(compiled.Runs, policy.Validated);
        Assert.All(compiled.Runs, compilation =>
        {
            Assert.Same(compilation.Desktop, compilation.NetWasm);
            Assert.Equal("patched-sha", compilation.Desktop.AssemblySha256);
            Assert.Contains("generated-regression-seed:42", compilation.Desktop.CompilerOptions);
            Assert.Equal([7, 9], compilation.Fixture.Inputs.AsEnumerable());
            Assert.Equal("source-sha", Assert.Single(compilation.Sources).Sha256);
        });
        for (var index = 0; index < patcher.Calls.Count; index++)
        {
            var patch = patcher.Calls[index];
            Assert.Equal(compiler.Calls[index].Desktop.AssemblyPath, patch.Path);
            Assert.Equal(compiler.Calls[index].Fixture.EntryType, patch.Type);
            Assert.Equal("Run", patch.Method);
            Assert.Equal([1, 2], patch.Bytes);
            Assert.Equal(8, patch.MaxStack);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidGeneratedRegressionDoesNotAllocateOrCompile(int defect)
    {
        var directories = new Directories();
        var compiler = new Compiler();
        var runner = Assert.IsAssignableFrom<IGeneratedCilRegressionRunner>(new GeneratedCilRegressionRunner(directories, compiler, new Patcher(), new Compiled(), new Policy(), new RecordingCorpusCleanup()));

        Assert.Throws<ArgumentException>(() => runner.RunRaw(1, defect == 0 ? [] : [1], defect == 1 ? default : defect == 2 ? [] : [0]));
        Assert.Equal(0, directories.Calls);
        Assert.Empty(compiler.Calls);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void FailedProfileRetainsTheWholeRunAndCleanupFailureRemainsVisible(bool generated, int failureStep)
    {
        var directories = new Directories();
        var compiler = new Compiler();
        var cause = new IOException("owned run failure");
        var compiled = new Compiled { Failure = failureStep < 2 ? cause : null, FailOnCall = failureStep + 1 };
        var cleanup = new RecordingCorpusCleanup { Failure = failureStep == 2 ? cause : null };

        void Run()
        {
            if (generated)
            {
                var runner = Assert.IsAssignableFrom<IGeneratedCilRegressionRunner>(new GeneratedCilRegressionRunner(
                    directories, compiler, new Patcher(), compiled, new Policy(), cleanup));
                runner.RunRaw(42, [1, 2], [0]);
            }
            else
            {
                var runner = Assert.IsAssignableFrom<ICfgPropertyRunner>(new CfgPropertyRunner(
                    directories, compiler, new Desktop(), new Comparer(), compiled, cleanup));
                runner.Run(Property());
            }
        }

        Assert.Same(cause, Assert.Throws<IOException>(Run));
        Assert.Equal("run-1", cause.Data["CorpusRunDirectory"]);
        Assert.Equal(failureStep == 0 ? 1 : 2, compiler.Calls.Count);
        Assert.Equal(compiler.Calls.Count, generated ? compiled.Runs.Count : compiled.Comparisons.Count);
        Assert.Equal(failureStep == 2 ? ["run-1"] : Array.Empty<string>(), cleanup.Directories);
    }

    private static void AssertPaths(List<CorpusCompilation> calls, Directories directories)
    {
        Assert.Equal(2, directories.Calls);
        Assert.Equal([CilProfile.Debug, CilProfile.Release, CilProfile.Debug, CilProfile.Release], calls.Select(call => call.Profile));
        Assert.Equal(4, calls.Select(call => call.Directory).Distinct().Count());
        Assert.Equal(Path.Combine("run-1", "Debug"), calls[0].Directory);
        Assert.Equal(Path.Combine("run-1", "Release"), calls[1].Directory);
        Assert.Equal(Path.Combine("run-2", "Debug"), calls[2].Directory);
        Assert.Equal(Path.Combine("run-2", "Release"), calls[3].Directory);
    }

    private static readonly OracleObservation Expected = new(OracleObservationKind.Value, 42, null, 0);
    private static CfgPropertyCase Property() => new("Cfg", 1, "shape", new("Cfg", "Cfg", "source", [0]), ImmutableDictionary<int, OracleObservation>.Empty.Add(0, Expected));

    private sealed class Directories : ICorpusRunDirectoryFactory
    {
        public int Calls { get; private set; }
        public string Create() => "run-" + ++Calls;
    }

    private sealed class Compiler : IRoslynCorpusCompiler
    {
        public List<CorpusCompilation> Calls { get; } = [];
        public CorpusCompilation Compile(CorpusFixture fixture, CilProfile profile, string outputDirectory)
        {
            var artifact = new CorpusArtifact(Path.Combine(outputDirectory, "input.dll"), "", "sha", "", "compiler", []);
            var result = new CorpusCompilation(fixture, profile, artifact, artifact, outputDirectory)
            {
                Sources = [new("Input.cs", Path.Combine(outputDirectory, "Input.cs"), "source-sha")],
            };
            Calls.Add(result);
            return result;
        }
    }

    private sealed class Desktop : IDesktopOracleRunner
    {
        public bool Wrong { get; init; }
        public int Calls { get; private set; }
        public ImmutableDictionary<int, OracleObservation> Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ImmutableDictionary<int, OracleObservation>.Empty.Add(0, Wrong ? Expected with { Value = 99 } : Expected);
        }
    }

    private sealed class Comparer : IOracleComparer
    {
        public OracleComparison Compare(OracleObservation desktop, OracleObservation netWasm) => new(desktop == netWasm, "comparison");
    }

    private sealed class WrongWasm64(bool skip) : INetWasmOracleRunner
    {
        public List<WasmTarget> Targets { get; } = [];
        public NetWasmExecution CompileAndRun(CorpusCompilation compilation, WasmTarget target, CancellationToken cancellationToken = default)
        {
            Targets.Add(target);
            return new(ImmutableDictionary<int, OracleObservation>.Empty.Add(0,
                target == WasmTarget.Wasm64 ? Expected with { Value = 99 } : Expected),
                null, "module", "sha", target, !(skip && target == WasmTarget.Wasm64));
        }
    }

    private sealed class Failures : ICompilerFailureArtifactWriter
    {
        public List<WasmTarget> Targets { get; } = [];
        public string Write(CorpusCompilation compilation, int input, OracleObservation desktop,
            OracleObservation netWasm, NetWasmExecution execution, string reason)
        {
            Targets.Add(execution.Target);
            Assert.Equal(42, desktop.Value);
            Assert.Equal(99, netWasm.Value);
            return "recorded-failure";
        }
    }

    private sealed class Compiled : ICompiledCorpusRunner, ICompiledCorpusComparisonRunner
    {
        public Exception? Failure { get; init; }
        public int FailOnCall { get; init; } = 1;
        public List<CorpusCompilation> Runs { get; } = [];
        public List<(CorpusCompilation Compilation, ImmutableDictionary<int, OracleObservation> Expected)> Comparisons { get; } = [];
        public void Run(CorpusCompilation compilation, CancellationToken cancellationToken = default)
        {
            Runs.Add(compilation);
            if (Failure is not null && Runs.Count == FailOnCall) throw Failure;
        }
        public void RunAgainstOracle(CorpusCompilation compilation, ImmutableDictionary<int, OracleObservation> expected, CancellationToken cancellationToken = default)
        {
            Comparisons.Add((compilation, expected));
            if (Failure is not null && Comparisons.Count == FailOnCall) throw Failure;
        }
    }

    private sealed class Patcher : IMethodBodyPatcher
    {
        public List<(string Path, string Type, string Method, byte[] Bytes, int MaxStack)> Calls { get; } = [];
        public PatchedMethodBody Patch(string assemblyPath, string typeName, string methodName, ReadOnlySpan<byte> cil, int maxStack)
        {
            Calls.Add((assemblyPath, typeName, methodName, cil.ToArray(), maxStack));
            return new(assemblyPath, "patched-sha", 20, cil.Length);
        }
        public PatchedMethodBody Patch(string assemblyPath, string typeName, string methodName, SerializedGeneratedMethod method, int maxStack) => throw new InvalidOperationException("Unexpected patch overload.");
    }

    private sealed class Policy : IOracleModePolicyRegistry, IOracleModePolicy
    {
        public List<CorpusCompilation> Validated { get; } = [];
        public OracleMode Mode => OracleMode.SameIl;
        public bool SharesPortableExecutable => true;
        public ImmutableDictionary<string, string> ReferenceAssemblyAliases => ImmutableDictionary<string, string>.Empty;
        public IOracleModePolicy Get(OracleMode mode)
        {
            Assert.Equal(OracleMode.SameIl, mode);
            return this;
        }
        public void Validate(CorpusFixture fixture) => throw new InvalidOperationException("Unexpected fixture validation.");
        public void ValidateCompilation(CorpusCompilation compilation) => Validated.Add(compilation);
    }
}
