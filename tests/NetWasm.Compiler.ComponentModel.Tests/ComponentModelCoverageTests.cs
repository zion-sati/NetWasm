using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.ComponentModel.Node;

using NetWasm.Wit.Bindings.TypeDefinitions;
using NetWasm.Wit.Bindings.FlatSlots;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentModelCoverageTests
{
    [Fact]
    public void CodeWriterFactoryCreatesIndependentWriters()
    {
        var factory = new CodeWriterFactory();
        var first = factory.Create();
        var second = factory.Create();

        Assert.NotSame(first, second);
        first.Line("first");
        second.Line("second");
        Assert.Equal("first\n", first.Text);
        Assert.Equal("second\n", second.Text);
    }

    [Fact]
    public void CodeWriterContractSupportsAllCommandsAndRejectsInvalidState()
    {
        var writer = new CodeWriter();
        var contract = (ICodeWriter)writer;

        contract.Write(new(CodeWriterCommandKind.Indent));
        contract.Write(new(CodeWriterCommandKind.Line, "value"));
        contract.Write(new(CodeWriterCommandKind.Raw, "raw"));
        contract.Write(new(CodeWriterCommandKind.Unindent));

        Assert.Equal("    value\nraw", contract.Text);
        Assert.Throws<InvalidOperationException>(() =>
            contract.Write(new(CodeWriterCommandKind.Unindent)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            contract.Write(new((CodeWriterCommandKind)99)));
    }

    [Fact]
    public void ComponentInputValidatorsRejectMissingAndNullInputs()
    {
        var core = new ComponentCoreModuleInputValidator(new SystemFileExistence());
        Assert.Throws<ArgumentNullException>(() => core.Validate(null!));
        Assert.Throws<CompilerException>(() => core.Validate(
            new ComponentCoreModuleLinkRequest(
                "missing-application.wasm",
                "missing-runtime.wasm",
                "output.wasm",
                ComponentTarget.Wasm32Wasi02)));

        var package = new ComponentPackageInputValidator(
            new SystemFileExistence(), new SystemDirectoryExistence());
        Assert.Throws<ArgumentNullException>(() => package.Validate(null!));
        Assert.Throws<CompilerException>(() => package.Validate(
            new ComponentPackageRequest(
                "missing-core.wasm",
                "missing.wit",
                null,
                "output.wasm",
                ComponentTarget.Wasm32Wasi02)));
    }

    [Fact]
    public void PackageInputValidatorAcceptsWitDirectoriesAndRequiresManagedRuntime()
    {
        using var files = new ComponentModelTestFiles();
        var core = files.Create("core.wasm", 0);
        var witDirectory = files.PathFor("wit");
        Directory.CreateDirectory(witDirectory);
        var validator = Assert.IsAssignableFrom<IComponentPackageInputValidator>(
            new ComponentPackageInputValidator(
                new SystemFileExistence(),
                new SystemDirectoryExistence()));
        var entryPoint = new NetWasm.Compiler.Core.ManagedExecutables.ManagedExecutableEntryPointAbi(
            NetWasm.Compiler.Core.ManagedExecutables.ManagedExecutableParameterShape.None,
            NetWasm.Compiler.Core.ManagedExecutables.ManagedExecutableReturnShape.Void);

        var exception = Assert.Throws<CompilerException>(() => validator.Validate(new(
            core,
            witDirectory,
            null,
            files.PathFor("output.wasm"),
            ComponentTarget.Wasm32Wasi02,
            ManagedExecutableEntryPoint: entryPoint)));
        Assert.Contains("requires a runtime core module", exception.Diagnostic.Message,
            StringComparison.Ordinal);

        validator.Validate(new(
            core,
            witDirectory,
            null,
            files.PathFor("output.wasm"),
            ComponentTarget.Wasm32Wasi02,
            "runtime.wasm",
            entryPoint));
    }

    [Fact]
    public void ProcessWasmToolsForwardsThroughExternalToolContract()
    {
        var runner = new RecordingProcessRunner();
        var tools = new ProcessWasmTools(runner, new ExternalToolCommand(
            "node",
            ["run-wasm-tools.mjs", "wasm-tools.wasm"]));

        var result = tools.Run(["component", "validate"]);

        Assert.Equal(new ToolResult(7, "output", "error"), result);
        Assert.Equal("node", runner.Executable);
        Assert.Equal(
            ["run-wasm-tools.mjs", "wasm-tools.wasm", "component", "validate"],
            runner.Arguments);
        _ = new ProcessWasmTools(runner).Run(["--version"]);
        Assert.Equal("wasm-tools", runner.Executable);
        Assert.Equal(["--version"], runner.Arguments);
        Assert.Throws<ArgumentNullException>(() => new ProcessWasmTools(null!));
        Assert.Throws<ArgumentException>(() => new ProcessWasmTools(runner, " "));
        Assert.Throws<ArgumentNullException>(() => new ProcessWasmTools(
            runner,
            (ExternalToolCommand)null!));
        Assert.Throws<ArgumentException>(() => new ProcessWasmTools(
            runner,
            new ExternalToolCommand("node", default)));
    }

    [Fact]
    public void OptimizerAndPackageValidatorRejectMissingPaths()
    {
        using var files = new ComponentModelTestFiles();
        var optimizer = new ComponentCoreModuleOptimizer(
            new RecordingProcessRunner(), new SystemFileExistence());
        Assert.Throws<CompilerException>(() => optimizer.Optimize(
            files.PathFor("missing.wasm"), files.PathFor("output.wasm"),
            ComponentTarget.Wasm32Wasi02));

        var core = files.Create("core.wasm", 0);
        var package = new ComponentPackageInputValidator(
            new SystemFileExistence(), new SystemDirectoryExistence());
        Assert.Throws<CompilerException>(() => package.Validate(
            new ComponentPackageRequest(
                core,
                files.PathFor("missing.wit"),
                null,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02)));
    }

    [Fact]
    public void ContractAdaptersRejectMissingCollaborators()
    {
        var merge = new NoopMerge();
        var environment = new NoopEnvironment();
        var host = new NoopHost();
        var managedExecutables = new NoopManagedExecutables();
        var exports = new NoopExports();
        var optimizer = new NoopOptimizer();
        var coreExecution = new NoopCoreExecution();
        var coreFactory = new ComponentCoreModuleWorkspaceFactory(new SystemFileDeleter());
        var coreValidator = new ComponentCoreModuleInputValidator(new SystemFileExistence());
        var packageOperations = new NoopPackageOperations();
        var coreLinker = new NoopLinker();
        var packageExecution = new NoopPackageExecution();
        var packageFactory = new ComponentPackageWorkspaceFactory(
            new SystemDirectoryCreator(), new SystemDirectoryDeleter());
        var packageValidator = new ComponentPackageInputValidator(
            new SystemFileExistence(), new SystemDirectoryExistence());
        var capability = new NoopCapability();
        var syntax = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());
        var imports = new NoopImports();
        var exportsSection = new NoopExportsSection();
        var flat = new NoopFlat();
        var marshallingTypes = new NoopMarshallingTypes();
        var declarations = new NoopDeclarations();
        var methods = new NoopMethods();
        var validator = new NoopWorldValidator();

        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleInputValidator(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentCoreModuleWorkspaceFactory(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentPackageInputValidator(null!, new SystemDirectoryExistence()));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentPackageInputValidator(new SystemFileExistence(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentPackageWorkspaceFactory(null!, new SystemDirectoryDeleter()));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentPackageWorkspaceFactory(new SystemDirectoryCreator(), null!));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(
            null!, new SystemByteFileReader(), new SystemByteFileWriter()));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(
            new SystemFileExistence(), null!, new SystemByteFileWriter()));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), null!));

        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinker(
            null!, coreFactory, coreExecution));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinker(
            coreValidator, null!, coreExecution));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinker(
            coreValidator, coreFactory, null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            null!, environment, host, managedExecutables, exports, optimizer));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            merge, null!, host, managedExecutables, exports, optimizer));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            merge, environment, null!, managedExecutables, exports, optimizer));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            merge, environment, host, null!, exports, optimizer));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            merge, environment, host, managedExecutables, null!, optimizer));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleLinkExecution(
            merge, environment, host, managedExecutables, exports, null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleMergeRunner(null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(
            null!, new SystemFileExistence()));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleOptimizer(
            new RecordingProcessRunner(), null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageExecution(
            null!, coreLinker, new SystemFileMover()));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageExecution(
            packageOperations, null!, new SystemFileMover()));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageExecution(
            packageOperations, coreLinker, null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageOperationRunner(null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackager(
            null!, packageFactory, packageExecution, capability));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackager(
            packageValidator, null!, packageExecution, capability));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackager(
            packageValidator, packageFactory, null!, capability));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackager(
            packageValidator, packageFactory, packageExecution, null!));
        Assert.Throws<ArgumentNullException>(() => new EmscriptenEnvironmentShimWriter(null!));
        Assert.Throws<ArgumentNullException>(() => new WasmTextModuleWriter(
            null!, new SystemTextFileWriter(), new SystemFileDeleter()));
        Assert.Throws<ArgumentNullException>(() => new WasmTextModuleWriter(
            new RecordingWasmTools(), null!, new SystemFileDeleter()));
        Assert.Throws<ArgumentNullException>(() => new WasmTextModuleWriter(
            new RecordingWasmTools(), new SystemTextFileWriter(), null!));
        Assert.Throws<ArgumentNullException>(() => new WitDocumentReader(null!));
        Assert.Throws<ArgumentNullException>(() => new WitDocumentReader(
            new RecordingWasmTools(), null!));
        Assert.Throws<ArgumentException>(() => new WitDocumentJsonReader().Read(""));
        Assert.Throws<ArgumentNullException>(() => new ComponentManifestBuilder(null!));
        Assert.Throws<ArgumentNullException>(() => new WitTypeDeclarationWriter(
            null!, new CodeWriterFactory(),
            new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())),
                new WitTypeDefinitionClassifier()));
        Assert.Throws<ArgumentNullException>(() => new WitTypeDeclarationWriter(
            syntax, null!,
            new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())),
                new WitTypeDefinitionClassifier()));
        Assert.Throws<ArgumentNullException>(() => new WitBindingMethodWriter(
            null!, exportsSection, flat));
        Assert.Throws<ArgumentNullException>(() => new WitBindingMethodWriter(
            imports, null!, flat));
        Assert.Throws<ArgumentNullException>(() => new WitBindingMethodWriter(
            imports, exportsSection, null!));
        var coreTypes = new WitCanonicalTypeResolver();
        var coreFlattener = new CanonicalAbiTypeFlattener();
        var coreSignatures = new CanonicalAbiSignaturePlanner(coreFlattener);
        var coreLayouts = new CanonicalAbiMemoryLayoutPlanner();
        var coreModels = new WitBindingFunctionModelBuilder(new WitCanonicalFunctionBuilder(coreTypes), coreSignatures);
        var slotCoercions = new WitFlatSlotCoercionFormatter();
        Assert.Throws<ArgumentNullException>(() => new WitBindingImportSectionWriter(
            coreTypes, coreFlattener, coreLayouts, coreModels, syntax,
            slotCoercions, null!));
        Assert.Throws<ArgumentNullException>(() => new WitBindingExportSectionWriter(
            coreTypes, coreFlattener, coreLayouts, coreModels, syntax,
            slotCoercions, null!));
        Assert.Throws<ArgumentNullException>(() => new WitBindingFlatSectionWriter(
            coreTypes, coreFlattener, coreLayouts, coreModels, syntax,
            slotCoercions, null!));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            null!, coreFlattener, coreLayouts, coreModels, syntax,
            slotCoercions));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            coreTypes, null!, coreLayouts, coreModels, syntax,
            slotCoercions));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            coreTypes, coreFlattener, null!, coreModels, syntax,
            slotCoercions));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            coreTypes, coreFlattener, coreLayouts, null!, syntax,
            slotCoercions));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            coreTypes, coreFlattener, coreLayouts, coreModels, null!,
            slotCoercions));
        Assert.Throws<ArgumentNullException>(() => new CoverageMethodSection(
            coreTypes, coreFlattener, coreLayouts, coreModels, syntax,
            null!));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingWriter(null!));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            null!, coreLayouts, new WitCanonicalTypeReachabilityResolver(), syntax,
            new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            coreTypes, null!, new WitCanonicalTypeReachabilityResolver(), syntax,
            new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            coreTypes, coreLayouts, null!, syntax, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            coreTypes, coreLayouts, new WitCanonicalTypeReachabilityResolver(), null!,
            new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCanonicalMarshallingTypeSectionWriter(
            coreTypes, coreLayouts, new WitCanonicalTypeReachabilityResolver(), syntax, null!));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            null!, declarations, methods, new WitCanonicalMarshallingWriter(marshallingTypes),
            syntax, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            validator, null!, methods, new WitCanonicalMarshallingWriter(marshallingTypes),
            syntax, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            validator, declarations, null!, new WitCanonicalMarshallingWriter(marshallingTypes),
            syntax, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            validator, declarations, methods, null!, syntax, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            validator, declarations, methods, new WitCanonicalMarshallingWriter(marshallingTypes),
            null!, new CodeWriterFactory()));
        Assert.Throws<ArgumentNullException>(() => new WitCSharpBindingGenerator(
            validator, declarations, methods, new WitCanonicalMarshallingWriter(marshallingTypes),
            syntax, null!));

        var methodWriter = new WitBindingMethodWriter(imports, exportsSection, flat);
        Assert.Throws<ArgumentNullException>(() => methodWriter.Generate(null!, new WitWorld(
            0, "world", "example:test@1.0.0", [], [])));
        Assert.Throws<ArgumentNullException>(() => methodWriter.Generate(
            new WitDocument([], [], [], [], "{}"), null!));
        var marshallingWriter = new WitCanonicalMarshallingWriter(marshallingTypes);
        Assert.Throws<ArgumentNullException>(() => marshallingWriter.Generate(null!, new WitWorld(
            0, "world", "example:test@1.0.0", [], [])));
        Assert.Throws<ArgumentNullException>(() => marshallingWriter.Generate(
            new WitDocument([], [], [], [], "{}"), null!));
        var generator = new WitCSharpBindingGenerator(
            validator,
            declarations,
            methods,
            marshallingWriter,
            syntax,
            new CodeWriterFactory());
        Assert.Throws<ArgumentNullException>(() => generator.Generate(null!, new WitWorld(
            0, "world", "example:test@1.0.0", [], [])));
        Assert.Throws<ArgumentNullException>(() => generator.Generate(
            new WitDocument([], [], [], [], "{}"), null!));
        var documentReader = new WitDocumentReader(new RecordingWasmTools());
        Assert.Throws<ArgumentException>(() => documentReader.Read(" "));
        var typeWriter = new WitTypeDeclarationWriter(syntax, new CodeWriterFactory(), new NetWasm.Wit.Bindings.Aliases.WitAliasValidator(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())), new WitTypeDefinitionClassifier());
        Assert.Throws<ArgumentNullException>(() => new WitTypeDeclarationWriter(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()), new CodeWriterFactory(), null!, new WitTypeDefinitionClassifier()));
        Assert.Throws<ArgumentNullException>(() => typeWriter.Generate(null!, new WitWorld(
            0, "world", "example:test@1.0.0", [], [])));
        Assert.Throws<ArgumentNullException>(() => typeWriter.Generate(
            new WitDocument([], [], [], [], "{}"), null!));

        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            null!, "environment", "host", "adapter", "merged", "sanitized"));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), null!, "host", "adapter", "merged", "sanitized"));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), "environment", null!, "adapter", "merged", "sanitized"));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), "environment", "host", null!, "merged", "sanitized"));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), "environment", "host", "adapter", null!, "sanitized"));
        Assert.Throws<ArgumentNullException>(() => new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), "environment", "host", "adapter", "merged", null!));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageWorkspace(
            null!, "temporary", "linked", "embedded", "component"));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageWorkspace(
            new SystemDirectoryDeleter(), null!, "linked", "embedded", "component"));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageWorkspace(
            new SystemDirectoryDeleter(), "temporary", null!, "embedded", "component"));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageWorkspace(
            new SystemDirectoryDeleter(), "temporary", "linked", null!, "component"));
        Assert.Throws<ArgumentNullException>(() => new ComponentPackageWorkspace(
            new SystemDirectoryDeleter(), "temporary", "linked", "embedded", null!));
    }

    [Fact]
    public void CoreLinkExecutionHandlesWasm64AndDisposesItsWorkspace()
    {
        using var files = new ComponentModelTestFiles();
        var environment = files.PathFor("environment.wasm");
        var merged = files.PathFor("merged.wasm");
        var sanitized = files.PathFor("sanitized.wasm");
        var workspace = new ComponentCoreModuleWorkspace(
            new SystemFileDeleter(), environment,
            files.PathFor("host.wasm"), files.PathFor("adapter.wasm"),
            merged, sanitized);
        var request = new ComponentCoreModuleLinkRequest(
            "application.wasm", "runtime.wasm", "output.wasm",
            ComponentTarget.Wasm64Wasi02);

        new ComponentCoreModuleLinkExecution(
                new NoopMerge(),
                new NoopEnvironment(),
                new NoopHost(),
                new NoopManagedExecutables(),
                new NoopExports(),
                new NoopOptimizer())
            .Run(request, workspace);

        Assert.False(File.Exists(environment));
    }

    private sealed class RecordingProcessRunner : IExternalToolRunner, IBinaryenToolRunner
    {
        public string Executable { get; private set; } = string.Empty;
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(string executable, IEnumerable<string> arguments)
        {
            Executable = executable;
            Arguments = arguments.ToArray();
            return new ToolResult(7, "output", "error");
        }

        public ToolResult Run(
            string toolId,
            System.Collections.Immutable.ImmutableArray<string> arguments) =>
            Run(toolId, (IEnumerable<string>)arguments);
    }

    private sealed class RecordingWasmTools : IWasmTools
    {
        public ToolResult Run(params IEnumerable<string> arguments) =>
            new(0, "{}", string.Empty);
    }

    private sealed class NoopMerge : IComponentCoreModuleMergeRunner
    {
        public void Run(ComponentCoreModuleMergeRequest request)
        {
        }
    }

    private sealed class NoopEnvironment : IEmscriptenEnvironmentShimWriter
    {
        public void Write(string outputPath, ComponentTarget target)
        {
        }
    }

    private sealed class NoopHost : INetWasmHostComponentShimWriter
    {
        public void Write(NetWasmHostComponentShimRequest request)
        {
        }
    }

    private sealed class NoopManagedExecutables : IManagedExecutableComponentAdapterWriter
    {
        public void Write(ManagedExecutableComponentAdapterRequest request)
        {
        }
    }

    private sealed class NoopExports : IWasmCoreModuleExportEditor
    {
        public void RetainComponentExports(string inputPath, string outputPath, string prefix)
        {
        }
    }

    private sealed class NoopOptimizer : IComponentCoreModuleOptimizer
    {
        public void Optimize(string inputPath, string outputPath, ComponentTarget target)
        {
        }
    }

    private sealed class NoopCoreExecution : IComponentCoreModuleLinkExecution
    {
        public void Run(ComponentCoreModuleLinkRequest request, ComponentCoreModuleWorkspace workspace)
        {
        }
    }

    private sealed class NoopPackageOperations : IComponentPackageOperationRunner
    {
        public void Run(IEnumerable<string> arguments, string operation)
        {
        }
    }

    private sealed class NoopLinker : IComponentCoreModuleLinker
    {
        public void Link(ComponentCoreModuleLinkRequest request)
        {
        }
    }

    private sealed class NoopPackageExecution : IComponentPackageExecution
    {
        public void Run(ComponentPackageRequest request, ComponentPackageWorkspace workspace)
        {
        }
    }

    private sealed class NoopCapability : IComponentPackagingCapability
    {
        public void EnsureSupported(ComponentTarget target)
        {
        }
    }

    private sealed class NoopImports : IWitBindingImportSectionWriter
    {
        public WitBindingSectionSource Generate(WitDocument document, WitWorld world) =>
            new(string.Empty, [], []);
    }

    private sealed class NoopExportsSection : IWitBindingExportSectionWriter
    {
        public WitBindingSectionSource Generate(WitDocument document, WitWorld world) =>
            new(string.Empty, [], []);
    }

    private sealed class NoopFlat : IWitBindingFlatSectionWriter
    {
        public WitBindingSectionSource Generate(WitDocument document,
            IReadOnlyCollection<int> lift, IReadOnlyCollection<int> lower) =>
            new(string.Empty, [], []);
    }

    private sealed class NoopMarshallingTypes : IWitCanonicalMarshallingTypeSectionWriter
    {
        public string Generate(WitDocument document, WitWorld world) => string.Empty;
    }

    private sealed class NoopDeclarations : IWitTypeDeclarationWriter
    {
        public string Generate(WitDocument document, WitWorld world) => string.Empty;
    }

    private sealed class NoopMethods : IWitBindingMethodWriter
    {
        public string Generate(WitDocument document, WitWorld world) => string.Empty;
    }

    private sealed class CoverageMethodSection(
        IWitCanonicalTypeResolver types,
        ICanonicalAbiTypeFlattener flattener,
        ICanonicalAbiMemoryLayoutPlanner layouts,
        IWitBindingFunctionModelBuilder models,
        IWitBindingSyntaxFormatter syntax,
        IWitFlatSlotCoercionFormatter slotCoercions) : WitBindingMethodSectionBase(
            types, flattener, layouts, models, syntax, slotCoercions);

    private sealed class NoopWorldValidator : IWitWorldValidator
    {
        public void Validate(WitDocument document, WitWorld world)
        {
        }
    }
}
