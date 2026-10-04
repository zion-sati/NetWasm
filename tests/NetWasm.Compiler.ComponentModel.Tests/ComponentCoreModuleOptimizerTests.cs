using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentCoreModuleOptimizerTests
{
    [Theory]
    [InlineData("wasm32", FinalWasmOptimization.None, null)]
    [InlineData("wasm64", FinalWasmOptimization.None, null)]
    [InlineData("wasm32", FinalWasmOptimization.O0, "-O0")]
    [InlineData("wasm32", FinalWasmOptimization.O1, "-O1")]
    [InlineData("wasm32", FinalWasmOptimization.O2, "-O2")]
    [InlineData("wasm32", FinalWasmOptimization.O3, "-O3")]
    [InlineData("wasm32", FinalWasmOptimization.Os, "-Os")]
    [InlineData("wasm32", FinalWasmOptimization.Oz, "-Oz")]
    [InlineData("wasm64", FinalWasmOptimization.Oz, "-Oz")]
    public void FinalizesWithSelectedPolicyAndTargetFeatures(
        string width, FinalWasmOptimization optimization, string? flag)
    {
        var stages = new RecordingStages();
        var target = width == "wasm64" ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;

        Create(stages).Optimize("input", "output", target, optimization);

        Assert.Equal(target, stages.ValidationTarget);
        Assert.Equal("input", stages.InspectedPath);
        if (flag is null)
        {
            Assert.Equal(["exists", "validate", "copy"], stages.Calls);
            Assert.Equal("input", stages.ValidationPath);
            Assert.Equal(("input", "output"), stages.Copy);
            Assert.Empty(stages.Arguments);
            return;
        }
        Assert.Equal(["exists", "optimize", "validate"], stages.Calls);
        Assert.Equal("output", stages.ValidationPath);
        Assert.Null(stages.Copy);
        var expected = new List<string>
        {
            "input", flag, "--converge", "--remove-unused-module-elements", "--strip-debug",
            "--enable-multimemory", "--enable-exception-handling", "--enable-reference-types", "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int",
        };
        if (width == "wasm64") expected.Add("--enable-memory64");
        expected.AddRange(["--output", "output"]);
        Assert.Equal(expected, stages.Arguments);
    }

    [Theory]
    [InlineData("bad module\n", "bad module")]
    [InlineData("", "unknown error")]
    public void OptimizerFailureStopsValidationAndCopy(string error, string expected)
    {
        var stages = new RecordingStages { Result = new(1, "", error) };

        var exception = Assert.Throws<CompilerException>(() => Create(stages).Optimize(
            "input", "output", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.Oz));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains(expected, exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(["exists", "optimize"], stages.Calls);
        Assert.Null(stages.ValidationPath);
        Assert.Null(stages.Copy);
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ValidationFailureStopsCopyOrReturn(FinalWasmOptimization optimization)
    {
        var failure = new CompilerException(new CompilerDiagnostic(DiagnosticCode.ComponentContract, "invalid core"));
        var stages = new RecordingStages { ValidationFailure = failure };

        Assert.Same(failure, Assert.Throws<CompilerException>(() => Create(stages).Optimize(
            "input", "output", ComponentTarget.Wasm64Wasi02, optimization)));

        Assert.Equal(optimization == FinalWasmOptimization.None ? "input" : "output", stages.ValidationPath);
        Assert.Equal(ComponentTarget.Wasm64Wasi02, stages.ValidationTarget);
        var expected = new List<string> { "exists" };
        if (optimization != FinalWasmOptimization.None) expected.Add("optimize");
        expected.Add("validate");
        Assert.Equal(expected, stages.Calls);
        Assert.Null(stages.Copy);
    }

    [Fact]
    public void NonePropagatesCopyFailureAfterValidationWithoutOptimization()
    {
        var failure = new IOException("copy failure");
        var stages = new RecordingStages { CopyFailure = failure };

        Assert.Same(failure, Assert.Throws<IOException>(() => Create(stages).Optimize(
            "input", "output", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None)));

        Assert.Equal(["exists", "validate", "copy"], stages.Calls);
    }

    [Fact]
    public void MissingInputStopsAllLaterCapabilities()
    {
        var stages = new RecordingStages { Exists = false };

        var exception = Assert.Throws<CompilerException>(() => Create(stages).Optimize(
            "input", "output", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.Oz));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal(["exists"], stages.Calls);
    }

    [Fact]
    public void InvalidArgumentsStopAllCapabilities()
    {
        var stages = new RecordingStages();
        var optimizer = Create(stages);
        Assert.Throws<ArgumentException>(() => optimizer.Optimize(" ", "output", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None));
        Assert.Throws<ArgumentNullException>(() => optimizer.Optimize(null!, "output", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None));
        Assert.Throws<ArgumentException>(() => optimizer.Optimize("input", " ", ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None));
        Assert.Throws<ArgumentNullException>(() => optimizer.Optimize("input", null!, ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None));
        Assert.Throws<ArgumentNullException>(() => optimizer.Optimize("input", "output", null!, FinalWasmOptimization.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize("input", "output", new("wasm128", "0.2", "utf8"), FinalWasmOptimization.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => optimizer.Optimize("input", "output", ComponentTarget.Wasm32Wasi02, (FinalWasmOptimization)42));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(null!, stages, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(stages, null!, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(stages, stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(stages, stages, stages, null!));
        Assert.Empty(stages.Calls);
    }

    private static IComponentCoreModuleOptimizer Create(RecordingStages stages) =>
        Assert.IsAssignableFrom<IComponentCoreModuleOptimizer>(
            new ComponentCoreModuleOptimizer(stages, stages, stages, stages));

    private sealed class RecordingStages : IBinaryenToolRunner, IFileExistence,
        IWasmCoreModuleValidator, IFileCopier
    {
        public List<string> Calls { get; } = [];
        public bool Exists { get; init; } = true;
        public ToolResult Result { get; init; } = new(0, "", "");
        public Exception? ValidationFailure { get; init; }
        public Exception? CopyFailure { get; init; }
        public string? InspectedPath { get; private set; }
        public string? ValidationPath { get; private set; }
        public ComponentTarget? ValidationTarget { get; private set; }
        public (string, string)? Copy { get; private set; }
        public ImmutableArray<string> Arguments { get; private set; } = [];

        bool IFileExistence.Exists(string path)
        {
            Calls.Add("exists");
            InspectedPath = path;
            return Exists;
        }

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            Assert.Equal(BinaryenToolIds.WasmOpt, toolId);
            Calls.Add("optimize");
            Arguments = arguments;
            return Result;
        }

        public void Validate(string path, ComponentTarget target)
        {
            Calls.Add("validate");
            ValidationPath = path;
            ValidationTarget = target;
            if (ValidationFailure is not null) throw ValidationFailure;
        }

        void IFileCopier.Copy(string sourcePath, string destinationPath)
        {
            Calls.Add("copy");
            Copy = (sourcePath, destinationPath);
            if (CopyFailure is not null) throw CopyFailure;
        }
    }
}
