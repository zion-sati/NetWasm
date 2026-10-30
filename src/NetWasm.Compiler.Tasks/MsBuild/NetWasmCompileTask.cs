using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

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

    public ITaskItem[] References { get; set; } = [];
    public ITaskItem[] Sources { get; set; } = [];
    public string Target { get; set; } = "wasm32";
    public string? ExceptionTypeMapPath { get; set; }
    public string? StackTraceSymbolsPath { get; set; }
    public string? DiagnosticTracePath { get; set; }
    public string? DiagnosticLogPath { get; set; }
    public string? WitPath { get; set; }
    public string? WitWorld { get; set; }
    // Modules the project's native archives import from (<NetWasmImportModule>).
    public ITaskItem[] ImportModules { get; set; } = [];
    public string? ProjectDirectory { get; set; }
    public string? PathMap { get; set; }
    public bool EmitStackTrace { get; set; }
    public bool StructuredDiagnostics { get; set; }
    public bool CompileAsLibrary { get; set; }
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
        if (!base.Execute())
        {
            return false;
        }
        Log.LogMessage(MessageImportance.High, "NetWasm: Wrote {0}.", Path.GetFullPath(CoreModulePath));
        return true;
    }

    internal string FullPathToTool => GenerateFullPathToTool();

    internal string CommandLineCommands => GenerateCommandLineCommands();

    internal string ResponseFileCommands => GenerateResponseFileCommands();

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
        builder.AppendSwitch(CompileAsLibrary ? "-target:library" : "-target:exe");
        builder.AppendSwitchIfNotNull("-platform:", Target);
        builder.AppendSwitchIfNotNull("-optimize:", Optimization);
        builder.AppendSwitchIfNotNull("-out:", CoreModulePath);
        foreach (var reference in References)
        {
            builder.AppendSwitchIfNotNull("-r:", reference.ItemSpec);
        }
        foreach (var source in Sources)
        {
            builder.AppendSwitchIfNotNull("-source:", source.ItemSpec);
        }
        builder.AppendSwitchIfNotNull("-runtimelayout:", RuntimeLayoutPath);
        builder.AppendSwitchIfNotNull("-interopmanifest:", InteropManifestPath);
        builder.AppendSwitchIfNotNull("-compilermetadata:", CompilerMetadataPath);
        builder.AppendSwitchIfNotNull("-callbackobject:", NativeCallbackObjectPath);
        AppendIfNotEmpty(builder, "-exceptiontypemap:", ExceptionTypeMapPath);
        if (EmitStackTrace)
        {
            AppendIfNotEmpty(builder, "-stacktracesymbols:", StackTraceSymbolsPath);
        }
        AppendIfNotEmpty(builder, "-wit:", WitPath);
        AppendIfNotEmpty(builder, "-world:", WitWorld);
        foreach (var module in ImportModules)
        {
            builder.AppendSwitchIfNotNull("-importmodule:", module.ItemSpec);
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
        AppendIfNotEmpty(builder, "-projectdirectory:", ProjectDirectory);
        AppendIfNotEmpty(builder, "-pathmap:", PathMap);
        builder.AppendSwitchIfNotNull("-intermediatedirectory:", IntermediateDirectory);
        builder.AppendSwitchIfNotNull("-wasmtoolsnode:", WasmToolsNodePath);
        builder.AppendSwitchIfNotNull("-wasmtoolscommand:", WasmToolsCommandPath);
        builder.AppendSwitchIfNotNull("-wasmtoolsmodule:", WasmToolsModulePath);
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
