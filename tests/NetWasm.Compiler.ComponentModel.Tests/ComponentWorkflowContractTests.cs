using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentWorkflowContractTests
{
    [Fact]
    public void CoreModuleLinkerDelegatesMergeExportAndOptimizationThroughContracts()
    {
        using var files = new ComponentModelTestFiles();
        var application = files.Create("application.wasm", 0);
        var runtime = files.Create("runtime.wasm", 0);
        var output = files.PathFor("component-core.wasm");
        var merge = new RecordingMergeRunner();
        var environment = new RecordingEnvironmentShim();
        var exports = new RecordingExports();
        var optimizer = new RecordingOptimizer();
        InvokeLink(
            AsLinker(new ComponentCoreModuleLinker(
                new ComponentCoreModuleInputValidator(new SystemFileExistence()),
                new ComponentCoreModuleWorkspaceFactory(new SystemFileDeleter()),
                new ComponentCoreModuleLinkExecution(
                    merge,
                    environment,
                    new RecordingHostShim(),
                    new RecordingManagedExecutableAdapter(),
                    exports,
                    optimizer))),
            application,
            runtime,
            output,
            ComponentTarget.Wasm32Wasi02);

        Assert.Equal(application, merge.Request!.ApplicationModulePath);
        Assert.Equal(runtime, merge.Request.RuntimeModulePath);
        Assert.Equal(output + ".sanitized.wasm", optimizer.InputPath);
        Assert.Equal(output, optimizer.OutputPath);
        Assert.Equal("cm32p2", exports.Prefix);
        Assert.Equal(1, environment.Writes);
    }

    [Fact]
    public void CoreModuleLinkerReportsToolFailureWithoutRunningLaterStages()
    {
        using var files = new ComponentModelTestFiles();
        var application = files.Create("application.wasm", 0);
        var runtime = files.Create("runtime.wasm", 0);
        var merge = new RecordingMergeRunner
        {
            Result = new ToolResult(1, string.Empty, "merge failed\n"),
        };
        var exports = new RecordingExports();
        var optimizer = new RecordingOptimizer();
        var exception = Assert.Throws<CompilerException>(() => InvokeLink(
            AsLinker(new ComponentCoreModuleLinker(
                new ComponentCoreModuleInputValidator(new SystemFileExistence()),
                new ComponentCoreModuleWorkspaceFactory(new SystemFileDeleter()),
                new ComponentCoreModuleLinkExecution(
                    merge,
                    new RecordingEnvironmentShim(),
                    new RecordingHostShim(),
                    new RecordingManagedExecutableAdapter(),
                    exports,
                    optimizer))),
            application,
            runtime,
            files.PathFor("component-core.wasm"),
            ComponentTarget.Wasm32Wasi02));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("merge failed", exception.Diagnostic.Message);
        Assert.Equal(0, exports.Calls);
        Assert.Equal(0, optimizer.Calls);
    }

    [Fact]
    public void CoreModuleLinkerAddsManagedExecutableHostAndCommandAdapters()
    {
        using var files = new ComponentModelTestFiles();
        var application = files.Create("application.wasm", 0);
        var runtime = files.Create("runtime.wasm", 0);
        var output = files.PathFor("component-core.wasm");
        var merge = new RecordingMergeRunner();
        var host = new RecordingHostShim();
        var adapter = new RecordingManagedExecutableAdapter();
        var entryPoint = new ManagedExecutableEntryPointAbi(
            ManagedExecutableParameterShape.StringArray,
            ManagedExecutableReturnShape.ExitCode);
        var linker = AsLinker(new ComponentCoreModuleLinker(
            new ComponentCoreModuleInputValidator(new SystemFileExistence()),
            new ComponentCoreModuleWorkspaceFactory(new SystemFileDeleter()),
            new ComponentCoreModuleLinkExecution(
                merge,
                new RecordingEnvironmentShim(),
                host,
                adapter,
                new RecordingExports(),
                new RecordingOptimizer())));

        linker.Link(new(
            application,
            runtime,
            output,
            ComponentTarget.Wasm32Wasi02,
            entryPoint));

        Assert.Equal(1, host.Writes);
        Assert.Equal(1, adapter.Writes);
        Assert.Equal(entryPoint, adapter.Request!.EntryPoint);
        Assert.Equal(output + ".netwasm-host.wasm", merge.Request!.HostModulePath);
        Assert.Equal(
            output + ".managed-executable.wasm",
            merge.Request.ManagedExecutableAdapterModulePath);
    }

    [Fact]
    public void PackagerRunsMetadataCreationValidationAndPublishesOutput()
    {
        using var files = new ComponentModelTestFiles();
        var core = files.Create("core.wasm", 0);
        var wit = files.Create("contract.wit", 0);
        var output = files.PathFor("component.wasm");
        var tools = new RecordingWasmTools();
        var capability = new RecordingCapability();
        var linker = new RecordingCoreModuleLinker();
        InvokePackage(
            AsPackager(new ComponentPackager(
                new ComponentPackageInputValidator(
                    new SystemFileExistence(), new SystemDirectoryExistence()),
                new ComponentPackageWorkspaceFactory(
                    new SystemDirectoryCreator(), new SystemDirectoryDeleter()),
                new ComponentPackageExecution(
                    new ComponentPackageOperationRunner(tools),
                    linker,
                    new SystemFileMover()),
                capability)),
            new ComponentPackageRequest(
                core,
                wit,
                "application",
                output,
                ComponentTarget.Wasm32Wasi02));

        Assert.True(File.Exists(output));
        Assert.Equal(ComponentTarget.Wasm32Wasi02, capability.Target);
        Assert.Equal(
            ["component embed", "component new", "validate"],
            tools.Operations);
        Assert.Equal(0, linker.Calls);
    }

    [Fact]
    public void PackagerLinksRuntimeBeforeEmbeddingWhenRequested()
    {
        using var files = new ComponentModelTestFiles();
        var core = files.Create("core.wasm", 0);
        var runtime = files.Create("runtime.wasm", 0);
        var wit = files.Create("contract.wit", 0);
        var output = files.PathFor("component.wasm");
        var linker = new RecordingCoreModuleLinker();
        InvokePackage(
            AsPackager(new ComponentPackager(
                new ComponentPackageInputValidator(
                    new SystemFileExistence(), new SystemDirectoryExistence()),
                new ComponentPackageWorkspaceFactory(
                    new SystemDirectoryCreator(), new SystemDirectoryDeleter()),
                new ComponentPackageExecution(
                    new ComponentPackageOperationRunner(new RecordingWasmTools()),
                    linker,
                    new SystemFileMover()),
                new RecordingCapability())),
            new ComponentPackageRequest(
                core,
                wit,
                null,
                output,
                ComponentTarget.Wasm32Wasi02,
                runtime));

        Assert.True(File.Exists(output));
        Assert.Equal(1, linker.Calls);
    }

    private static void InvokeLink(
        IComponentCoreModuleLinker linker,
        string application,
        string runtime,
        string output,
        ComponentTarget target) => linker.Link(new(
            application,
            runtime,
            output,
            target));

    private static void InvokePackage(
        IComponentPackager packager,
        ComponentPackageRequest request) => packager.Package(request);

    private static IComponentCoreModuleLinker AsLinker(object linker) =>
        (IComponentCoreModuleLinker)linker;

    private static IComponentPackager AsPackager(object packager) =>
        (IComponentPackager)packager;

    private sealed class RecordingMergeRunner : IComponentCoreModuleMergeRunner
    {
        public ToolResult Result { get; init; } = new(0, string.Empty, string.Empty);
        public ComponentCoreModuleMergeRequest? Request { get; private set; }

        public void Run(ComponentCoreModuleMergeRequest request)
        {
            Request = request;
            if (Result.ExitCode != 0)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.ComponentToolchain,
                    Result.StandardError));
            }
        }
    }

    private sealed class RecordingWasmTools : IWasmTools
    {
        public List<string> Operations { get; } = [];

        public ToolResult Run(params IEnumerable<string> arguments)
        {
            var values = arguments.ToArray();
            Operations.Add(values[0] switch
            {
                "component" when values[1] == "embed" => "component embed",
                "component" when values[1] == "new" => "component new",
                "validate" => "validate",
                _ => string.Join(' ', values),
            });
            if (values.Contains("--output"))
            {
                var output = values[Array.IndexOf(values, "--output") + 1];
                File.WriteAllBytes(output, [0]);
            }
            return new(0, string.Empty, string.Empty);
        }
    }

    private sealed class RecordingEnvironmentShim : IEmscriptenEnvironmentShimWriter
    {
        public int Writes { get; private set; }

        public void Write(string outputPath, ComponentTarget target) => Writes++;
    }

    private sealed class RecordingExports : IWasmCoreModuleExportEditor
    {
        public int Calls { get; private set; }
        public string Prefix { get; private set; } = string.Empty;

        public void RetainComponentExports(
            string inputPath,
            string outputPath,
            string prefix)
        {
            Calls++;
            Prefix = prefix;
        }
    }

    private sealed class RecordingOptimizer : IComponentCoreModuleOptimizer
    {
        public int Calls { get; private set; }
        public string InputPath { get; private set; } = string.Empty;
        public string OutputPath { get; private set; } = string.Empty;

        public void Optimize(
            string inputPath,
            string outputPath,
            ComponentTarget target,
            FinalWasmOptimization optimization)
        {
            Calls++;
            InputPath = inputPath;
            OutputPath = outputPath;
        }
    }

    private sealed class RecordingCapability : IComponentPackagingCapability
    {
        public ComponentTarget? Target { get; private set; }

        public void EnsureSupported(ComponentTarget target) => Target = target;
    }

    private sealed class RecordingCoreModuleLinker : IComponentCoreModuleLinker
    {
        public int Calls { get; private set; }

        public void Link(ComponentCoreModuleLinkRequest request) => Calls++;
    }

    private sealed class RecordingHostShim : INetWasmHostComponentShimWriter
    {
        public int Writes { get; private set; }

        public void Write(NetWasmHostComponentShimRequest request) => Writes++;
    }

    private sealed class RecordingManagedExecutableAdapter :
        IManagedExecutableComponentAdapterWriter
    {
        public int Writes { get; private set; }
        public ManagedExecutableComponentAdapterRequest? Request { get; private set; }

        public void Write(ManagedExecutableComponentAdapterRequest request)
        {
            Writes++;
            Request = request;
        }
    }
}
