using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Inlining;
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

    private static readonly ComponentCoreModuleMergeIntermediates Steps =
        new("sanitized.wasm", "runtime-sanitized.wasm", "merged.wasm");

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
            target)
        { Intermediates = Steps };

        InvokeMerge(
            AsMergeRunner(new ComponentCoreModuleMergeRunner(tools, new RecordingSteps(), new RecordingSteps())),
            request);

        Assert.Equal(BinaryenToolIds.WasmMerge, tools.ToolId);
        Assert.Equal(memory64, tools.Arguments.Contains("--enable-memory64"));
        Assert.DoesNotContain("wasi_snapshot_preview1", tools.Arguments);
        Assert.DoesNotContain("libc.wasm", tools.Arguments);
        var expected = new List<string>
        {
            "runtime-sanitized.wasm", "netwasm.runtime.v1",
            "sanitized.wasm", "netwasm.application.v1",
            "environment.wasm", "env",
            "--output", "merged.wasm",
            "--enable-multimemory", "--enable-exception-handling",
            "--enable-reference-types",
            "--enable-bulk-memory", "--enable-nontrapping-float-to-int",
            "--enable-simd",
        };
        if (memory64) expected.Add("--enable-memory64");
        Assert.Equal(expected, tools.Arguments);
    }

    // wasm-merge keeps every input as it is, so the hints are selected into intermediate modules
    // before it, and the exports after it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreModuleMergeRunnerSelectsHintsBeforeAndExportsAfterTheMerge(bool exports)
    {
        var tools = new RecordingExternalTools();
        var steps = new RecordingSteps();
        WasmExportSelection selection = new("cm32p2", []);

        InvokeMerge(
            AsMergeRunner(new ComponentCoreModuleMergeRunner(tools, steps, steps)),
            new ComponentCoreModuleMergeRequest("application.wasm", "runtime.wasm", "environment.wasm",
                "output.wasm", ComponentTarget.Wasm32Wasi02)
            {
                ApplicationInlineAnnotations = WasmInlineAnnotationSelection.SizeAware,
                RuntimeInlineAnnotations = WasmInlineAnnotationSelection.Remove,
                Exports = exports ? selection : null,
                Intermediates = Steps,
            });

        string[] expected =
        [
            "hints application.wasm -> sanitized.wasm SizeAware",
            "hints runtime.wasm -> runtime-sanitized.wasm Remove",
            .. exports ? ["exports merged.wasm -> output.wasm cm32p2"] : Array.Empty<string>(),
        ];
        Assert.Equal(expected, steps.Calls);
        Assert.Equal(exports ? "merged.wasm" : "output.wasm",
            tools.Arguments[tools.Arguments.ToList().IndexOf("--output") + 1]);
    }

    [Fact]
    public void CoreModuleMergeRunnerNeedsItsIntermediatesAndEditors()
    {
        var tools = new RecordingExternalTools();
        var steps = new RecordingSteps();
        var runner = new ComponentCoreModuleMergeRunner(tools, steps, steps);

        var exception = Assert.Throws<CompilerException>(() => runner.Run(new(
            "application.wasm", "runtime.wasm", "environment.wasm", "merged.wasm", ComponentTarget.Wasm32Wasi02)));

        Assert.Contains("needs its intermediate modules", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(steps.Calls);
        Assert.Throws<ArgumentNullException>(() => runner.Run(null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleMergeRunner(null!, steps, steps));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleMergeRunner(tools, null!, steps));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleMergeRunner(tools, steps, null!));
    }

    [Fact]
    public void CoreModuleMergeRunnerReportsFailure()
    {
        var runner = new ComponentCoreModuleMergeRunner(
            new RecordingExternalTools
            {
                Result = new ToolResult(1, string.Empty, "merge\r\nfailed"),
            }, new RecordingSteps(), new RecordingSteps());

        var exception = Assert.Throws<CompilerException>(() => InvokeMerge(
            AsMergeRunner(runner),
            new ComponentCoreModuleMergeRequest(
                "application.wasm",
                "runtime.wasm",
                "environment.wasm",
                "merged.wasm",
                ComponentTarget.Wasm32Wasi02)
            { Intermediates = Steps }));

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
            "adapter.wasm")
        { Intermediates = Steps };

        InvokeMerge(
            AsMergeRunner(new ComponentCoreModuleMergeRunner(tools, new RecordingSteps(), new RecordingSteps())),
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
            new RecordingExternalTools { Result = new(1, string.Empty, string.Empty) },
            new RecordingSteps(), new RecordingSteps());

        var exception = Assert.Throws<CompilerException>(() => InvokeMerge(
            AsMergeRunner(runner),
            new ComponentCoreModuleMergeRequest(
                "application.wasm",
                "runtime.wasm",
                "environment.wasm",
                "merged.wasm",
                ComponentTarget.Wasm32Wasi02)
            { Intermediates = Steps }));

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

    private sealed class RecordingSteps : IWasmCoreModuleInlineAnnotationEditor, IWasmCoreModuleExportEditor
    {
        public List<string> Calls { get; } = [];

        public void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection) =>
            Calls.Add($"hints {inputPath} -> {outputPath} {selection}");

        public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection) =>
            Calls.Add($"exports {inputPath} -> {outputPath} {selection.ComponentPrefix}");
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
