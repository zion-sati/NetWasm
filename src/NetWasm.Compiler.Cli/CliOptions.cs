using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

// What nwc builds, as csc's -target: an executable with a managed entry point,
// a library, or a module whose entry is one static method (-entry).
internal enum NwcTarget
{
    Exe,
    Library,
    Function,
}

internal sealed record CompileCliOptions(
    string Input,
    string Output,
    NwcTarget Target,
    string EntryType,
    string EntryMethod,
    ImmutableArray<string> References,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Platform)
{
    public static readonly FrozenSet<string> Names = new[]
    {
        "out", "reference", "r", "target", "entry", "export", "platform", "optimize", "pathmap", "source", "interopmanifest", "runtimelayout", "compilermetadata",
        "exceptiontypemap", "stacktracesymbols", "callbackobject", "wit", "world", "importmodule",
        "diagnostictrace", "diagnosticlog", "jsexportboundary", "structureddiagnostics",
        "projectdirectory", "intermediatedirectory", "wasmtoolsnode", "wasmtoolscommand",
        "wasmtoolsmodule", "managedloopinvariantfieldhoisting", "managedstaticdataviewelimination",
        "managedaddresstakenvaluepromotion", "managedmethodinlining", "managedbranchprefixduplication",
        "managedscalarcallpreservation", "managedarrayboundscheckelimination", "managedloopunrolling",
        "managedconstantargumentpropagation", "managedstaticarraypopulationelimination",
        "managedconstantcallfolding", "managedswitchbittests", "manageddevirtualization",
        "managedallocationelimination", "managedlooprotation", "managedarraystorecheckelimination",
        "manageddispatcherelimination", "managedemptyfinallyremoval", "managedstructscalarreplacement", "managedallocationscalarreplacement",
        "managedidempotentloopcollapse", "managedloopstoreforwarding", "managedarrayloopidioms", "managedloopvectorization",
    }.ToFrozenSet(StringComparer.Ordinal);

    public FinalWasmOptimization Optimization { get; init; } = FinalWasmOptimization.Oz;
    public string? PathMap { get; init; }
    public ImmutableArray<string> Sources { get; init; } = [];
    public string? InteropManifest { get; init; }
    public string? RuntimeLayout { get; init; }
    public string? CompilerMetadata { get; init; }
    public string? ExceptionTypeMap { get; init; }
    public string? StackTraceSymbols { get; init; }
    public string? CallbackObject { get; init; }
    public string? Wit { get; init; }
    public string? World { get; init; }
    // Modules the input's native archives import from; the host supplies them.
    public ImmutableArray<string> ImportModules { get; init; } = [];
    public string? DiagnosticTrace { get; init; }
    public string? DiagnosticLog { get; init; }
    public bool UseJavaScriptExportBoundary { get; init; }
    public bool StructuredDiagnostics { get; init; }
    public string? ProjectDirectory { get; init; }
    public string? IntermediateDirectory { get; init; }
    public ExternalToolCommand? WasmTools { get; init; }
    public string? ManagedLoopInvariantFieldHoisting { get; init; }
    public string? ManagedStaticDataViewElimination { get; init; }
    public string? ManagedAddressTakenValuePromotion { get; init; }
    public string? ManagedMethodInlining { get; init; }
    public string? ManagedBranchPrefixDuplication { get; init; }
    public string? ManagedScalarCallPreservation { get; init; }
    public string? ManagedArrayBoundsCheckElimination { get; init; }
    public string? ManagedLoopUnrolling { get; init; }
    public string? ManagedConstantArgumentPropagation { get; init; }
    public string? ManagedStaticArrayPopulationElimination { get; init; }
    public string? ManagedConstantCallFolding { get; init; }
    public string? ManagedSwitchBitTests { get; init; }
    public string? ManagedDevirtualization { get; init; }
    public string? ManagedAllocationElimination { get; init; }
    public string? ManagedLoopRotation { get; init; }
    public string? ManagedArrayStoreCheckElimination { get; init; }
    public string? ManagedDispatcherElimination { get; init; }
    public string? ManagedEmptyFinallyRemoval { get; init; }
    public string? ManagedStructScalarReplacement { get; init; }
    public string? ManagedAllocationScalarReplacement { get; init; }
    public string? ManagedIdempotentLoopCollapse { get; init; }
    public string? ManagedLoopStoreForwarding { get; init; }
    public string? ManagedArrayLoopIdioms { get; init; }
    public string? ManagedLoopVectorization { get; init; }

    public static CompileCliOptions Parse(NwcArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new NwcOptionValues(arguments);
        var input = arguments.Inputs.Length switch
        {
            1 => arguments.Inputs[0],
            0 => throw CliOptionException.Create("no input assembly was specified"),
            _ => throw CliOptionException.Create(
                $"only one input assembly can be compiled: {string.Join(", ", arguments.Inputs)}"),
        };
        var target = options.Single("target") switch
        {
            null or "exe" => NwcTarget.Exe,
            "library" => NwcTarget.Library,
            "function" => NwcTarget.Function,
            _ => throw CliOptionException.Create("-target must be 'exe', 'library' or 'function'"),
        };
        var entry = options.Single("entry");
        (var entryType, var entryMethod) = target == NwcTarget.Function
            ? ParseMethod(entry ?? throw CliOptionException.Create("-target:function requires -entry:Type::Method"))
            : entry is null
                ? (string.Empty, string.Empty)
                : throw CliOptionException.Create("-entry is only valid with -target:function");
        return new(
            input,
            options.Single("out") ?? throw CliOptionException.Create("no output file was specified (-out)"),
            target,
            entryType,
            entryMethod,
            options.List("reference", "r"),
            [.. options.Many("export").Select(ParseExport)],
            ParsePlatform(options.Single("platform")))
        {
            Optimization = ParseOptimization(options.Single("optimize")),
            PathMap = options.Single("pathmap"),
            Sources = options.Many("source"),
            InteropManifest = options.Single("interopmanifest"),
            RuntimeLayout = options.Single("runtimelayout"),
            CompilerMetadata = options.Single("compilermetadata"),
            ExceptionTypeMap = options.Single("exceptiontypemap"),
            StackTraceSymbols = options.Single("stacktracesymbols"),
            CallbackObject = options.Single("callbackobject"),
            Wit = options.Single("wit"),
            World = options.Single("world"),
            ImportModules = options.Many("importmodule"),
            DiagnosticTrace = options.Single("diagnostictrace"),
            DiagnosticLog = options.Single("diagnosticlog"),
            UseJavaScriptExportBoundary = options.Toggle("jsexportboundary"),
            StructuredDiagnostics = options.Toggle("structureddiagnostics"),
            ProjectDirectory = options.Single("projectdirectory"),
            IntermediateDirectory = options.Single("intermediatedirectory"),
            WasmTools = ParseWasmTools(
                options.Single("wasmtoolsnode"),
                options.Single("wasmtoolscommand"),
                options.Single("wasmtoolsmodule")),
            ManagedLoopInvariantFieldHoisting = options.Single("managedloopinvariantfieldhoisting"),
            ManagedStaticDataViewElimination = options.Single("managedstaticdataviewelimination"),
            ManagedAddressTakenValuePromotion = options.Single("managedaddresstakenvaluepromotion"),
            ManagedMethodInlining = options.Single("managedmethodinlining"),
            ManagedBranchPrefixDuplication = options.Single("managedbranchprefixduplication"),
            ManagedScalarCallPreservation = options.Single("managedscalarcallpreservation"),
            ManagedArrayBoundsCheckElimination = options.Single("managedarrayboundscheckelimination"),
            ManagedLoopUnrolling = options.Single("managedloopunrolling"),
            ManagedConstantArgumentPropagation = options.Single("managedconstantargumentpropagation"),
            ManagedStaticArrayPopulationElimination = options.Single("managedstaticarraypopulationelimination"),
            ManagedConstantCallFolding = options.Single("managedconstantcallfolding"),
            ManagedSwitchBitTests = options.Single("managedswitchbittests"),
            ManagedDevirtualization = options.Single("manageddevirtualization"),
            ManagedAllocationElimination = options.Single("managedallocationelimination"),
            ManagedLoopRotation = options.Single("managedlooprotation"),
            ManagedArrayStoreCheckElimination = options.Single("managedarraystorecheckelimination"),
            ManagedDispatcherElimination = options.Single("manageddispatcherelimination"),
            ManagedEmptyFinallyRemoval = options.Single("managedemptyfinallyremoval"),
            ManagedStructScalarReplacement = options.Single("managedstructscalarreplacement"),
            ManagedAllocationScalarReplacement = options.Single("managedallocationscalarreplacement"),
            ManagedIdempotentLoopCollapse = options.Single("managedidempotentloopcollapse"),
            ManagedLoopStoreForwarding = options.Single("managedloopstoreforwarding"),
            ManagedArrayLoopIdioms = options.Single("managedarrayloopidioms"),
            ManagedLoopVectorization = options.Single("managedloopvectorization"),
        };
    }

    // wasm-tools runs as the toolchain's JavaScript build under node; without
    // these, nwc runs the wasm-tools on the PATH.
    private static ExternalToolCommand? ParseWasmTools(string? node, string? command, string? module) =>
        (node, command, module) switch
        {
            (null, null, null) => null,
            ({ } executable, { } script, { } component) =>
                new(executable, ["--disable-warning=ExperimentalWarning", script, component]),
            _ => throw CliOptionException.Create(
                "-wasmtoolsnode, -wasmtoolscommand and -wasmtoolsmodule must be given together"),
        };

    private static RequestedExport ParseExport(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw CliOptionException.Create("-export must use 'name=Namespace.Type::Method' syntax");
        }
        var (type, method) = ParseMethod(value[(separator + 1)..]);
        return new RequestedExport(value[..separator], type, method);
    }

    private static (string Type, string Method) ParseMethod(string value)
    {
        var parts = value.Split("::", 2, StringSplitOptions.None);
        return parts.Length == 2 && parts[0].Length != 0 && parts[1].Length != 0
            ? (parts[0], parts[1])
            : throw CliOptionException.Create("a method must use 'Namespace.Type::Method' syntax");
    }

    internal static WasmTarget ParsePlatform(string? value) => value switch
    {
        null or "wasm32" => WasmTarget.Wasm32,
        "wasm64" => WasmTarget.Wasm64,
        _ => throw CliOptionException.Create("-platform must be 'wasm32' or 'wasm64'"),
    };

    internal static FinalWasmOptimization ParseOptimization(string? value) => value switch
    {
        null or "Oz" => FinalWasmOptimization.Oz,
        "None" or "none" => FinalWasmOptimization.None,
        "O0" => FinalWasmOptimization.O0,
        "O1" => FinalWasmOptimization.O1,
        "O2" => FinalWasmOptimization.O2,
        "O3" => FinalWasmOptimization.O3,
        "Os" => FinalWasmOptimization.Os,
        _ => throw CliOptionException.Create(
            "-optimize must be 'None', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'"),
    };
}

internal sealed record ComponentizeCliOptions(
    string CoreModule,
    string? RuntimeModule,
    string Wit,
    string? World,
    string Output,
    string Manifest,
    WasmTarget Platform,
    string? InteropManifest,
    string? JcoVersion,
    string? Preview2ShimVersion,
    FinalWasmOptimization Optimization)
{
    public const string CommandName = "componentize";

    public static readonly FrozenSet<string> Names = new[]
    {
        "coremodule", "runtimemodule", "wit", "world", "out", "manifest", "platform",
        "interopmanifest", "jcoversion", "preview2shimversion", "optimize", "managedinlininghints",
    }.ToFrozenSet(StringComparer.Ordinal);

    public string? ManagedInliningHints { get; init; }

    public static ComponentizeCliOptions Parse(NwcArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Inputs.Length != 0)
        {
            throw CliOptionException.Create(
                $"componentize takes no input files: {string.Join(", ", arguments.Inputs)}");
        }
        var options = new NwcOptionValues(arguments);
        return new(
            options.Single("coremodule") ?? throw CliOptionException.Create("no core module was specified (-coremodule)"),
            options.Single("runtimemodule"),
            options.Single("wit") ?? throw CliOptionException.Create("no WIT package was specified (-wit)"),
            options.Single("world"),
            options.Single("out") ?? throw CliOptionException.Create("no output file was specified (-out)"),
            options.Single("manifest") ?? throw CliOptionException.Create("no manifest file was specified (-manifest)"),
            CompileCliOptions.ParsePlatform(options.Single("platform")),
            options.Single("interopmanifest"),
            options.Single("jcoversion"),
            options.Single("preview2shimversion"),
            CompileCliOptions.ParseOptimization(options.Single("optimize")))
        {
            ManagedInliningHints = options.Single("managedinlininghints"),
        };
    }
}

// Reads the options of one command: each value option at most once, a list
// option any number of times, a flag without a value, and a toggle as -name,
// -name+ or -name-.
internal sealed class NwcOptionValues(NwcArguments arguments)
{
    private readonly ILookup<string, NwcOption> _options = arguments.Options.ToLookup(option => option.Name);

    public string? Single(string name)
    {
        var options = _options[name].ToArray();
        return options.Length switch
        {
            0 => null,
            1 => Value(options[0]),
            _ => throw CliOptionException.Create($"option '-{name}' was specified more than once"),
        };
    }

    public ImmutableArray<string> Many(string name) => [.. _options[name].Select(Value)];

    // A reference list, as csc reads one: several per option, separated by ',' or
    // ';' outside quotes, in command-line order whichever of the names each
    // option uses.
    public ImmutableArray<string> List(params string[] names) =>
    [
        .. arguments.Options.Where(option => names.Contains(option.Name))
            .SelectMany(option => NwcArgumentReader.SplitList(RequireValue(option))),
    ];

    public bool Flag(string name)
    {
        foreach (var option in _options[name])
        {
            if (option.Value is not null)
            {
                throw CliOptionException.Create($"option '{option.Spelling}' takes no value");
            }
        }
        return _options[name].Any();
    }

    public bool Toggle(string name) => _options[name].LastOrDefault() switch
    {
        null => false,
        { Value: null or "+" } => true,
        { Value: "-" } => false,
        var option => throw CliOptionException.Create($"option '{option.Spelling}' takes '+' or '-', not a value"),
    };

    private static string Value(NwcOption option) => NwcArgumentReader.Unquote(RequireValue(option)) is { Length: > 0 } value
        ? value
        : throw RequiresAValue(option);

    // The value an option was written with, quotes included.
    private static string RequireValue(NwcOption option) => option.Value is { Length: > 0 } value && value is not ("+" or "-")
        ? value
        : throw RequiresAValue(option);

    private static CompilerException RequiresAValue(NwcOption option) =>
        CliOptionException.Create($"option '{option.Spelling}' requires a value");
}
