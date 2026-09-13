using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentOperationRunnerTests
{
    [Fact]
    public void PackageOperationRunnerDelegatesArgumentsAndOperation()
    {
        var tools = new RecordingWasmTools();

        InvokePackageOperation(
            AsPackageOperationRunner(new ComponentPackageOperationRunner(tools)),
            ["component", "new", "input.wasm"],
            "create component");

        Assert.Equal(["component", "new", "input.wasm"], tools.Arguments);
    }

    [Fact]
    public void PackageOperationRunnerNormalizesFailureOutput()
    {
        var runner = new ComponentPackageOperationRunner(
            new RecordingWasmTools
            {
                Result = new ToolResult(1, string.Empty, "bad\r\ncomponent\n"),
            });

        var exception = Assert.Throws<CompilerException>(() => InvokePackageOperation(
            AsPackageOperationRunner(runner),
            ["component", "new"],
            "create component"));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("bad  component", exception.Diagnostic.Message);
    }

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm64", true)]
    public void CoreModuleMergeRunnerSelectsTargetFeatures(string width, bool memory64)
    {
        var tools = new RecordingExternalTools();
        var target = width == "wasm64"
            ? ComponentTarget.Wasm64Wasi02
            : ComponentTarget.Wasm32Wasi02;
        var request = new ComponentCoreModuleMergeRequest(
            "application.wasm",
            "runtime.wasm",
            "environment.wasm",
            "merged.wasm",
            target);

        InvokeMerge(
            AsMergeRunner(new ComponentCoreModuleMergeRunner(tools)),
            request);

        Assert.Equal(BinaryenToolIds.WasmMerge, tools.ToolId);
        Assert.Equal(memory64, tools.Arguments.Contains("--enable-memory64"));
        Assert.DoesNotContain("wasi_snapshot_preview1", tools.Arguments);
        Assert.DoesNotContain("libc.wasm", tools.Arguments);
        var expected = new List<string>
        {
            "runtime.wasm", "netwasm.runtime.v1",
            "application.wasm", "netwasm.application.v1",
            "environment.wasm", "env",
            "--output", "merged.wasm",
            "--enable-multimemory", "--enable-exception-handling",
            "--enable-bulk-memory", "--enable-nontrapping-float-to-int",
        };
        if (memory64) expected.Add("--enable-memory64");
        Assert.Equal(expected, tools.Arguments);
    }

    [Fact]
    public void CoreModuleMergeRunnerReportsFailure()
    {
        var runner = new ComponentCoreModuleMergeRunner(
            new RecordingExternalTools
            {
                Result = new ToolResult(1, string.Empty, "merge\r\nfailed"),
            });

        var exception = Assert.Throws<CompilerException>(() => InvokeMerge(
            AsMergeRunner(runner),
            new ComponentCoreModuleMergeRequest(
                "application.wasm",
                "runtime.wasm",
                "environment.wasm",
                "merged.wasm",
                ComponentTarget.Wasm32Wasi02)));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("merge  failed", exception.Diagnostic.Message);
    }

    [Fact]
    public void CoreModuleMergeRunnerAddsOptionalManagedExecutableModules()
    {
        var tools = new RecordingExternalTools();
        var request = new ComponentCoreModuleMergeRequest(
            "application.wasm",
            "runtime.wasm",
            "environment.wasm",
            "merged.wasm",
            ComponentTarget.Wasm32Wasi02,
            "host.wasm",
            "adapter.wasm");

        InvokeMerge(
            AsMergeRunner(new ComponentCoreModuleMergeRunner(tools)),
            request);

        Assert.Contains("host.wasm", tools.Arguments);
        Assert.Contains(RuntimeAbi.HostModule, tools.Arguments);
        Assert.Contains("adapter.wasm", tools.Arguments);
        Assert.Contains("netwasm.command.v1", tools.Arguments);
    }

    [Fact]
    public void CoreModuleMergeRunnerReportsUnknownFailureWithoutErrorOutput()
    {
        var runner = new ComponentCoreModuleMergeRunner(
            new RecordingExternalTools { Result = new(1, string.Empty, string.Empty) });

        var exception = Assert.Throws<CompilerException>(() => InvokeMerge(
            AsMergeRunner(runner),
            new ComponentCoreModuleMergeRequest(
                "application.wasm",
                "runtime.wasm",
                "environment.wasm",
                "merged.wasm",
                ComponentTarget.Wasm32Wasi02)));

        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageOperationRunnerReportsUnknownFailureWithoutErrorOutput()
    {
        var runner = new ComponentPackageOperationRunner(
            new RecordingWasmTools { Result = new(1, string.Empty, string.Empty) });

        var exception = Assert.Throws<CompilerException>(() => InvokePackageOperation(
            AsPackageOperationRunner(runner), ["component", "new"], "create component"));

        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    private static void InvokePackageOperation(
        IComponentPackageOperationRunner runner,
        IEnumerable<string> arguments,
        string operation) => runner.Run(arguments, operation);

    private static void InvokeMerge(
        IComponentCoreModuleMergeRunner runner,
        ComponentCoreModuleMergeRequest request) => runner.Run(request);

    private static IComponentPackageOperationRunner AsPackageOperationRunner(object value) =>
        (IComponentPackageOperationRunner)value;

    private static IComponentCoreModuleMergeRunner AsMergeRunner(object value) =>
        (IComponentCoreModuleMergeRunner)value;

    private sealed class RecordingWasmTools : IWasmTools
    {
        public ToolResult Result { get; init; } = new(0, string.Empty, string.Empty);
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(params IEnumerable<string> arguments)
        {
            Arguments = arguments.ToArray();
            return Result;
        }
    }

    private sealed class RecordingExternalTools : IBinaryenToolRunner
    {
        public ToolResult Result { get; init; } = new(0, string.Empty, string.Empty);
        public string ToolId { get; private set; } = string.Empty;
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            ToolId = toolId;
            Arguments = arguments.ToArray();
            return Result;
        }
    }
}
