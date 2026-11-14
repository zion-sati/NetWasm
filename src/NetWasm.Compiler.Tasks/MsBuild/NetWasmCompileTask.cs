using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.MsBuild;

// Runs nwc, the NetWasm compiler, as the Csc task runs csc: every option goes in
// a response file, and each line nwc writes is logged, its banner and stages at
// high importance and its "error NW1234: message" lines as errors.
public sealed class NetWasmCompileTask : ToolTask
{
    public NetWasmCompileTask() => LogStandardErrorAsError = true;

    // nwc's native executable, which the host tools provide, or nwc.dll, which
    // the dotnet host on the PATH runs, as a source build does.
    [Required]
    public string CompilerPath { get; set; } = string.Empty;

    [Required]
    public string InputAssemblyPath { get; set; } = string.Empty;

    [Required]
    public string CoreModulePath { get; set; } = string.Empty;

    [Required]
    public string RuntimeLayoutPath { get; set; } = string.Empty;

    [Required]
    public string InteropManifestPath { get; set; } = string.Empty;

    [Required]
    public string CompilerMetadataPath { get; set; } = string.Empty;

    [Required]
    public string NativeCallbackObjectPath { get; set; } = string.Empty;

    // Where nwc caches the compiled metadata of unchanged references.
    [Required]
    public string IntermediateDirectory { get; set; } = string.Empty;

    [Required]
    public string WasmToolsNodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    // The host tools' native wasm-tools of the same release, which nwc runs in place of the WASI one.
    public string NativeWasmToolsPath { get; set; } = string.Empty;

    public ITaskItem[] References { get; set; } = [];

    // Reference facades the compiler reads for type-forwarding metadata and never executes. One that also
    // appears in References is compiled as metadata only.
    public ITaskItem[] MetadataReferences { get; set; } = [];

    private readonly CompilerReferenceOriginReader _referenceOrigins = new();

    private readonly CompilerOriginalContractReferenceReader _originalContracts = new();
    public ITaskItem[] Sources { get; set; } = [];
    public string Target { get; set; } = "wasm32";
    public string? TypeSymbolsPath { get; set; }
    public string? MethodSymbolsPath { get; set; }
    public string? DiagnosticTracePath { get; set; }
    public string? DiagnosticLogPath { get; set; }
    public string? WitPath { get; set; }
    public string? WitWorld { get; set; }
    // Modules the project's native archives import from (<NetWasmImportModule>).
    public ITaskItem[] ImportModules { get; set; } = [];
    // The program's <NativeLibrary> items. With LinkedNativeOnly, a P/Invoke into a library none of them names for
    // the target (NetWasmLibraryName) compiles to a body that throws DllNotFoundException, as Mono's WebAssembly
    // build fails calls into modules it does not link.
    public ITaskItem[] NativeLibraries { get; set; } = [];
    public bool LinkedNativeOnly { get; set; }
    // The project's RuntimeHostConfigurationOption items; those marked Trim="true" with a Boolean value are folded,
    // as ILLink folds them.
    public ITaskItem[] RuntimeHostConfigurationOptions { get; set; } = [];
    public string? ProjectDirectory { get; set; }
    public string? PathMap { get; set; }
    public bool EmitStackTrace { get; set; }
    public bool EmitTypeNames { get; set; }
    public bool StructuredDiagnostics { get; set; }
    // Keep and reuse compiled units (NetWasmCompiledUnits: on by default at O0), and verify reused ones.
    // "true" or "false"; empty leaves nwc's default (on at O0).
    public string? CompiledUnits { get; set; }
    public bool VerifyCompiledUnits { get; set; }
    public bool CompileAsLibrary { get; set; }
    // A plain core module of the selected WIT world, without WASI (-target:coremodule).
    public bool PlainCoreModule { get; set; }
    public bool UseJavaScriptExportBoundary { get; set; }
    public bool NoLogo { get; set; }
    public string Optimization { get; set; } = "Oz";
    public string ManagedLoopInvariantFieldHoisting { get; set; } = "Auto";
    public string ManagedStaticDataViewElimination { get; set; } = "Auto";
    public string ManagedAddressTakenValuePromotion { get; set; } = "Auto";
    public string ManagedMethodInlining { get; set; } = "Auto";
    public string ManagedBranchPrefixDuplication { get; set; } = "Auto";
    public string ManagedScalarCallPreservation { get; set; } = "Auto";
    public string ManagedArrayBoundsCheckElimination { get; set; } = "Auto";
    public string ManagedLoopUnrolling { get; set; } = "Auto";
    public string ManagedConstantArgumentPropagation { get; set; } = "Auto";
    public string ManagedStaticArrayPopulationElimination { get; set; } = "Auto";
    public string ManagedConstantCallFolding { get; set; } = "Auto";
    public string ManagedSwitchBitTests { get; set; } = "Auto";
    public string ManagedDevirtualization { get; set; } = "Auto";
    public string ManagedAllocationElimination { get; set; } = "Auto";
    public string ManagedLoopRotation { get; set; } = "Auto";
    public string ManagedArrayStoreCheckElimination { get; set; } = "Auto";
    public string ManagedDispatcherElimination { get; set; } = "Auto";
    public string ManagedEmptyFinallyRemoval { get; set; } = "Auto";
    public string ManagedStructScalarReplacement { get; set; } = "Auto";
    public string ManagedAllocationScalarReplacement { get; set; } = "Auto";
    public string ManagedIdempotentLoopCollapse { get; set; } = "Auto";
    public string ManagedLoopStoreForwarding { get; set; } = "Auto";
    public string ManagedArrayLoopIdioms { get; set; } = "Auto";
    public string ManagedLoopVectorization { get; set; } = "Auto";
    public string ManagedPureCallReuse { get; set; } = "Auto";
    public string ManagedFieldLoadReuse { get; set; } = "Auto";
    public string ManagedConditionalStoreMerging { get; set; } = "Auto";
    public string ManagedInlineArrayData { get; set; } = "Auto";
    public string ManagedQuotientReuse { get; set; } = "Auto";
    public string ManagedIntegerCheckElimination { get; set; } = "Auto";
    public string ManagedLoopInvariantExpressionHoisting { get; set; } = "Auto";
    public string ManagedMulticastInlineDispatch { get; set; } = "Auto";
    public string ManagedNewObjectNullCheckElimination { get; set; } = "Auto";
    public string ManagedLoopStrengthReduction { get; set; } = "Auto";
    public string ManagedRangeBranchFolding { get; set; } = "Auto";
    public string ManagedUnwrittenStaticFieldFolding { get; set; } = "Auto";

    protected override string ToolName => "nwc";

    protected override MessageImportance StandardOutputLoggingImportance => MessageImportance.High;

    private bool RunsOnTheDotNetHost => CompilerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    public override bool Execute()
    {
        Log.LogMessage(
            MessageImportance.High,
            "NetWasm: Compiling {0} for {1}...",
            Path.GetFileName(InputAssemblyPath),
            Target);
        EnvironmentVariables = ToolEnvironment();
        if (!base.Execute())
        {
            return false;
        }
        Log.LogMessage(MessageImportance.High, "NetWasm: Wrote {0}.", Path.GetFullPath(CoreModulePath));
        return true;
    }

    // A compile allocates about a gigabyte and exits: gen0 budgets of 256 MB per heap, with
    // large arrays allocated there too, let it finish without collecting at all. The runtime
    // reads these only from the environment, in hex; a build or shell that sets either keeps
    // its own.
    private static readonly (string Name, string Value)[] GarbageCollection =
    [
        ("DOTNET_GCgen0size", "10000000"),
        ("DOTNET_GCLOHThreshold", "8000000"),
    ];

    internal string[] ToolEnvironment()
    {
        var variables = new List<string>(EnvironmentVariables ?? []);
        foreach (var (name, value) in GarbageCollection)
        {
            if (Environment.GetEnvironmentVariable(name) is null &&
                !variables.Exists(variable => variable.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)))
            {
                variables.Add($"{name}={value}");
            }
        }
        return [.. variables];
    }

    internal string FullPathToTool => GenerateFullPathToTool();

    internal string CommandLineCommands => GenerateCommandLineCommands();

    internal string ResponseFileCommands => GenerateResponseFileCommands();

    // Trim="true" options with a Boolean value, the last of each name as MSBuild items resolve, in name order.
    private IEnumerable<(string Name, bool Value)> FoldedFeatureSwitches() => RuntimeHostConfigurationOptions
        .Where(item => string.Equals(item.GetMetadata("Trim"), "true", StringComparison.OrdinalIgnoreCase) &&
            bool.TryParse(item.GetMetadata("Value"), out _))
        .GroupBy(item => item.ItemSpec, StringComparer.Ordinal)
        .Select(group => (group.Key, bool.Parse(group.Last().GetMetadata("Value"))))
        .OrderBy(pair => pair.Key, StringComparer.Ordinal);

    // Every option, as runtimeconfig.json carries it: the last value of each name, in name order.
    private IEnumerable<(string Name, string Value)> RuntimeProperties() => RuntimeHostConfigurationOptions
        .GroupBy(item => item.ItemSpec, StringComparer.Ordinal)
        .Select(group => (group.Key, group.Last().GetMetadata("Value")))
        .OrderBy(pair => pair.Key, StringComparer.Ordinal);

    // The libraries a <NativeLibrary> item provides for this target, each once.
    private IEnumerable<string> LinkedLibraryNames() => NativeLibraries
        .Where(item => item.GetMetadata("WasmTarget") is var target &&
            (string.IsNullOrEmpty(target) || string.Equals(target, Target, StringComparison.OrdinalIgnoreCase)))
        .Select(item => item.GetMetadata("NetWasmLibraryName"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal);

    protected override string GenerateFullPathToTool() => RunsOnTheDotNetHost ? "dotnet" : CompilerPath;

    protected override string GenerateCommandLineCommands()
    {
        var builder = new CommandLineBuilder();
        if (RunsOnTheDotNetHost)
        {
            builder.AppendFileNameIfNotNull(CompilerPath);
        }
        return builder.ToString();
    }

    protected override string GenerateResponseFileCommands()
    {
        var builder = new CommandLineBuilder(quoteHyphensOnCommandLine: false, useNewLineSeparator: true);
        if (NoLogo)
        {
            builder.AppendSwitch("-nologo");
        }
        builder.AppendFileNameIfNotNull(InputAssemblyPath);
        builder.AppendSwitch(PlainCoreModule
            ? "-target:coremodule"
            : CompileAsLibrary ? "-target:library" : "-target:exe");
        builder.AppendSwitchIfNotNull("-platform:", Target);
        builder.AppendSwitchIfNotNull("-optimize:", Optimization);
        builder.AppendSwitchIfNotNull("-out:", CoreModulePath);
        var metadataPaths = MetadataReferences.Select(static item => item.ItemSpec).ToImmutableHashSet(StringComparer.Ordinal);
        var implementationReferences = References.Where(item => !metadataPaths.Contains(item.ItemSpec)).ToArray();
        foreach (var reference in implementationReferences)
        {
            builder.AppendSwitchIfNotNull("-r:", reference.ItemSpec);
        }
        foreach (var reference in MetadataReferences)
        {
            builder.AppendSwitchIfNotNull("-metadatareference:", reference.ItemSpec);
        }
        foreach (var contract in _originalContracts.Read(implementationReferences.Concat(MetadataReferences)))
        {
            builder.AppendSwitchIfNotNull("-originalcontract:", contract);
        }
        // Package provenance, for the diagnostics that name a reference's package: the path, then only the
        // facts the item carries.
        foreach (var origin in _referenceOrigins.Read(implementationReferences, CompilationReferenceRole.Implementation)
            .AddRange(_referenceOrigins.Read(MetadataReferences, CompilationReferenceRole.Metadata)))
        {
            string?[] facts =
            [
                origin.PackageId is null ? null : "package=" + origin.PackageId,
                origin.PackageVersion is null ? null : "version=" + origin.PackageVersion,
                origin.AssetPath is null ? null : "asset=" + origin.AssetPath,
                origin.OriginalReferencePath is null ? null : "original=" + origin.OriginalReferencePath,
            ];
            if (facts.Any(fact => fact is not null))
            {
                builder.AppendSwitchIfNotNull("-referenceorigin:", [origin.Path, .. facts.Where(fact => fact is not null)!], ";");
            }
        }
        foreach (var source in Sources)
        {
            builder.AppendSwitchIfNotNull("-source:", source.ItemSpec);
        }
        builder.AppendSwitchIfNotNull("-runtimelayout:", RuntimeLayoutPath);
        builder.AppendSwitchIfNotNull("-interopmanifest:", InteropManifestPath);
        builder.AppendSwitchIfNotNull("-compilermetadata:", CompilerMetadataPath);
        builder.AppendSwitchIfNotNull("-callbackobject:", NativeCallbackObjectPath);
        AppendIfNotEmpty(builder, "-typesymbols:", TypeSymbolsPath);
        if (EmitStackTrace)
        {
            AppendIfNotEmpty(builder, "-methodsymbols:", MethodSymbolsPath);
        }
        if (EmitTypeNames)
        {
            builder.AppendSwitch("-typenames+");
        }
        AppendIfNotEmpty(builder, "-wit:", WitPath);
        AppendIfNotEmpty(builder, "-world:", WitWorld);
        foreach (var module in ImportModules)
        {
            builder.AppendSwitchIfNotNull("-importmodule:", module.ItemSpec);
        }
        foreach (var (name, value) in FoldedFeatureSwitches())
        {
            builder.AppendSwitchIfNotNull("-featureswitch:", $"{name}={(value ? "true" : "false")}");
        }
        foreach (var (name, value) in RuntimeProperties())
        {
            builder.AppendSwitchIfNotNull("-runtimeproperty:", $"{name}={value}");
        }
        if (LinkedNativeOnly)
        {
            builder.AppendSwitch("-linkednativeonly+");
            foreach (var library in LinkedLibraryNames())
            {
                builder.AppendSwitchIfNotNull("-nativelibrary:", library);
            }
        }
        AppendIfNotEmpty(builder, "-diagnostictrace:", DiagnosticTracePath);
        AppendIfNotEmpty(builder, "-diagnosticlog:", DiagnosticLogPath);
        if (UseJavaScriptExportBoundary)
        {
            builder.AppendSwitch("-jsexportboundary+");
        }
        if (StructuredDiagnostics)
        {
            builder.AppendSwitch("-structureddiagnostics+");
        }
        if (bool.TryParse(CompiledUnits, out var compiledUnits))
        {
            builder.AppendSwitch(compiledUnits ? "-compiledunits+" : "-compiledunits-");
        }
        if (VerifyCompiledUnits)
        {
            builder.AppendSwitch("-verifycompiledunits+");
        }
        AppendIfNotEmpty(builder, "-projectdirectory:", ProjectDirectory);
        AppendIfNotEmpty(builder, "-pathmap:", PathMap);
        builder.AppendSwitchIfNotNull("-intermediatedirectory:", IntermediateDirectory);
        builder.AppendSwitchIfNotNull("-wasmtoolsnode:", WasmToolsNodePath);
        builder.AppendSwitchIfNotNull("-wasmtoolscommand:", WasmToolsCommandPath);
        builder.AppendSwitchIfNotNull("-wasmtoolsmodule:", WasmToolsModulePath);
        AppendIfNotEmpty(builder, "-wasmtools:", NativeWasmToolsPath);
        // Auto is nwc's own default for each managed optimization.
        foreach (var (name, value) in new[]
                 {
                     ("loopinvariantfieldhoisting", ManagedLoopInvariantFieldHoisting),
                     ("staticdataviewelimination", ManagedStaticDataViewElimination),
                     ("addresstakenvaluepromotion", ManagedAddressTakenValuePromotion),
                     ("methodinlining", ManagedMethodInlining),
                     ("branchprefixduplication", ManagedBranchPrefixDuplication),
                     ("scalarcallpreservation", ManagedScalarCallPreservation),
                     ("arrayboundscheckelimination", ManagedArrayBoundsCheckElimination),
                     ("loopunrolling", ManagedLoopUnrolling),
                     ("constantargumentpropagation", ManagedConstantArgumentPropagation),
                     ("staticarraypopulationelimination", ManagedStaticArrayPopulationElimination),
                     ("constantcallfolding", ManagedConstantCallFolding),
                     ("switchbittests", ManagedSwitchBitTests),
                     ("devirtualization", ManagedDevirtualization),
                     ("allocationelimination", ManagedAllocationElimination),
                     ("looprotation", ManagedLoopRotation),
                     ("arraystorecheckelimination", ManagedArrayStoreCheckElimination),
                     ("dispatcherelimination", ManagedDispatcherElimination),
                     ("emptyfinallyremoval", ManagedEmptyFinallyRemoval),
                     ("structscalarreplacement", ManagedStructScalarReplacement),
                     ("allocationscalarreplacement", ManagedAllocationScalarReplacement),
                     ("idempotentloopcollapse", ManagedIdempotentLoopCollapse),
                     ("loopstoreforwarding", ManagedLoopStoreForwarding),
                     ("arrayloopidioms", ManagedArrayLoopIdioms),
                     ("loopvectorization", ManagedLoopVectorization),
                     ("purecallreuse", ManagedPureCallReuse),
                     ("fieldloadreuse", ManagedFieldLoadReuse),
                     ("conditionalstoremerging", ManagedConditionalStoreMerging),
                     ("inlinearraydata", ManagedInlineArrayData),
                     ("quotientreuse", ManagedQuotientReuse),
                     ("integercheckelimination", ManagedIntegerCheckElimination),
                     ("loopinvariantexpressionhoisting", ManagedLoopInvariantExpressionHoisting),
                     ("multicastinlinedispatch", ManagedMulticastInlineDispatch),
                     ("newobjectnullcheckelimination", ManagedNewObjectNullCheckElimination),
                     ("loopstrengthreduction", ManagedLoopStrengthReduction),
                     ("rangebranchfolding", ManagedRangeBranchFolding),
                     ("unwrittenstaticfieldfolding", ManagedUnwrittenStaticFieldFolding),
                 })
        {
            if (value is not ("" or "Auto"))
            {
                builder.AppendSwitchIfNotNull($"-managed{name}:", value);
            }
        }
        return builder.ToString();
    }

    private static void AppendIfNotEmpty(CommandLineBuilder builder, string option, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            builder.AppendSwitchIfNotNull(option, value);
        }
    }
}
