using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentCoreModuleOptimizerTests
{
    [Theory]
    [InlineData("wasm32", false, FinalWasmOptimization.None, false)]
    [InlineData("wasm32", false, FinalWasmOptimization.Size, true)]
    [InlineData("wasm64", true, FinalWasmOptimization.None, false)]
    [InlineData("wasm64", true, FinalWasmOptimization.Size, true)]
    public void FinalizesWithSelectedPolicyAndTargetFeatures(
        string width,
        bool memory64,
        FinalWasmOptimization optimization,
        bool sizeOptimization)
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0x2a);
        var output = files.PathFor("output.wasm");
        var tools = new RecordingTools();
        var validator = new RecordingValidator();
        var target = width == "wasm64"
            ? ComponentTarget.Wasm64Wasi02
            : ComponentTarget.Wasm32Wasi02;

        new ComponentCoreModuleOptimizer(tools, new SystemFileExistence(), validator,
            new SystemFileCopier())
            .Optimize(input, output, target, optimization);

        if (!sizeOptimization)
        {
            Assert.Equal(string.Empty, tools.ToolId);
            Assert.Equal(input, validator.Path);
            Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
            return;
        }
        Assert.Null(validator.Path);
        Assert.Equal(BinaryenToolIds.WasmOpt, tools.ToolId);
        Assert.Contains("-Oz", tools.Arguments);
        Assert.Contains("--remove-unused-module-elements", tools.Arguments);
        Assert.Equal(memory64, tools.Arguments.Contains("--enable-memory64"));
        Assert.Equal(["--output", output], tools.Arguments[^2..]);
    }

    [Fact]
    public void ReportsOptimizerFailure()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var tools = new RecordingTools { Result = new(1, "", "bad module\n") };

        var exception = Assert.Throws<CompilerException>(() =>
            Create(tools).Optimize(
                input,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02,
                FinalWasmOptimization.Size));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("bad module", exception.Diagnostic.Message);
    }

    [Fact]
    public void ReportsUnknownOptimizerFailureWhenToolHasNoError()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var tools = new RecordingTools { Result = new(1, "", "") };

        var exception = Assert.Throws<CompilerException>(() =>
            Create(tools).Optimize(
                input,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02,
                FinalWasmOptimization.Size));

        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUndefinedOptimizationBeforeRunningTool()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var tools = new RecordingTools();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(tools).Optimize(
                input,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02,
                (FinalWasmOptimization)42));

        Assert.Equal(string.Empty, tools.ToolId);
    }

    [Fact]
    public void RejectsMissingInputBeforeRunningTool()
    {
        using var files = new ComponentModelTestFiles();
        var tools = new RecordingTools();

        var exception = Assert.Throws<CompilerException>(() =>
            Create(tools).Optimize(
                files.PathFor("missing.wasm"),
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02,
                FinalWasmOptimization.None));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal(string.Empty, tools.ToolId);
    }

    [Fact]
    public void NonePropagatesCopyFailureWithoutRunningOptimizer()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0x2a);
        var tools = new RecordingTools();
        var copies = new ThrowingCopier();

        Assert.Same(copies.Failure, Assert.Throws<IOException>(() =>
            new ComponentCoreModuleOptimizer(tools, new SystemFileExistence(),
                new RecordingValidator(), copies).Optimize(
                input, files.PathFor("output.wasm"), ComponentTarget.Wasm32Wasi02,
                FinalWasmOptimization.None)));

        Assert.Equal(string.Empty, tools.ToolId);
    }

    [Fact]
    public void NonePropagatesValidationFailureBeforeCopyingOrRunningOptimizer()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0x2a);
        var output = files.PathFor("output.wasm");
        var tools = new RecordingTools();
        var validator = new RecordingValidator
        {
            Failure = new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentToolchain, "invalid core")),
        };

        Assert.Same(validator.Failure, Assert.Throws<CompilerException>(() =>
            new ComponentCoreModuleOptimizer(tools, new SystemFileExistence(), validator,
                new SystemFileCopier()).Optimize(input, output,
                    ComponentTarget.Wasm32Wasi02, FinalWasmOptimization.None)));

        Assert.Equal(input, validator.Path);
        Assert.False(File.Exists(output));
        Assert.Equal(string.Empty, tools.ToolId);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleOptimizer(null!, new SystemFileExistence(),
                new RecordingValidator(), new SystemFileCopier()));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleOptimizer(new RecordingTools(), null!,
                new RecordingValidator(), new SystemFileCopier()));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleOptimizer(new RecordingTools(), new SystemFileExistence(),
                null!, new SystemFileCopier()));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleOptimizer(new RecordingTools(), new SystemFileExistence(),
                new RecordingValidator(), null!));
    }

    private static ComponentCoreModuleOptimizer Create(RecordingTools tools) => new(
        tools, new SystemFileExistence(), new RecordingValidator(), new SystemFileCopier());

    private sealed class RecordingTools : IBinaryenToolRunner
    {
        public ToolResult Result { get; init; } = new(0, "", "");
        public string ToolId { get; private set; } = "";
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            ToolId = toolId;
            Arguments = arguments.ToArray();
            return Result;
        }
    }

    private sealed class ThrowingCopier : IFileCopier
    {
        public IOException Failure { get; } = new("copy failure");

        public void Copy(string sourcePath, string destinationPath) => throw Failure;
    }

    private sealed class RecordingValidator : IWasmCoreModuleValidator
    {
        public CompilerException? Failure { get; init; }
        public string? Path { get; private set; }

        public void Validate(string path)
        {
            Path = path;
            if (Failure is not null) throw Failure;
        }
    }
}
