using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompileCliOptionsTests
{
    [Fact]
    public void DefaultsEverythingButTheInputAndOutput()
    {
        var options = Parse("App.dll", "-out:app.wasm");

        Assert.Equal("App.dll", options.Input);
        Assert.Equal("app.wasm", options.Output);
        Assert.Equal(NwcTarget.Exe, options.Target);
        Assert.Empty(options.EntryType);
        Assert.Empty(options.EntryMethod);
        Assert.Empty(options.References);
        Assert.Empty(options.Exports);
        Assert.Equal(WasmTarget.Wasm32, options.Platform);
        Assert.Equal(FinalWasmOptimization.Oz, options.Optimization);
        Assert.Empty(options.Sources);
        Assert.Empty(options.ImportModules);
        Assert.False(options.UseJavaScriptExportBoundary);
        Assert.False(options.StructuredDiagnostics);
        Assert.Null(options.WasmTools);
        Assert.Null(options.RuntimeLayout);
        Assert.Null(options.CallbackObject);
        Assert.Null(options.ManagedMethodInlining);
    }

    [Fact]
    public void ReadsEveryOption()
    {
        var options = Parse(
            "-target:function", "-entry:Example.Entry::Run", "App.dll", "-out:app.wasm",
            "-reference:first.dll,second.dll", "-r:third.dll;fourth.dll", "-r:fifth.dll",
            "-export:add=Example.Math::Add", "-export:sub=Example.Math::Sub", "-platform:wasm64",
            "-optimize:O3", "-pathmap:/src=/_/", "-source:Entry.cs", "-source:Math.cs",
            "-interopmanifest:interop.json", "-runtimelayout:layout.json", "-compilermetadata:metadata.json",
            "-exceptiontypemap:exceptions.json", "-stacktracesymbols:symbols.json",
            "-callbackobject:callbacks.o", "-wit:contract.wit", "-world:example:test/test",
            "-importmodule:host_ui", "-importmodule:host_gl", "-diagnostictrace:trace.json",
            "-diagnosticlog:compiler.jsonl", "-jsexportboundary+", "-structureddiagnostics",
            "-projectdirectory:/project", "-intermediatedirectory:/project/obj",
            "-wasmtoolsnode:node", "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
            "-managedloopinvariantfieldhoisting:Disabled", "-managedstaticdataviewelimination:Enabled",
            "-managedaddresstakenvaluepromotion:Enabled", "-managedmethodinlining:Disabled",
            "-managedbranchprefixduplication:Disabled", "-managedscalarcallpreservation:Enabled",
            "-managedarrayboundscheckelimination:Disabled", "-managedloopunrolling:Enabled",
            "-managedconstantargumentpropagation:Enabled", "-managedstaticarraypopulationelimination:Disabled",
            "-managedconstantcallfolding:Enabled", "-managedswitchbittests:Disabled",
            "-manageddevirtualization:Enabled", "-managedallocationelimination:Disabled",
            "-managedlooprotation:Disabled", "-managedarraystorecheckelimination:Enabled");

        Assert.Equal(NwcTarget.Function, options.Target);
        Assert.Equal("Example.Entry", options.EntryType);
        Assert.Equal("Run", options.EntryMethod);
        Assert.Equal(["first.dll", "second.dll", "third.dll", "fourth.dll", "fifth.dll"], options.References);
        Assert.Equal(
            [new RequestedExport("add", "Example.Math", "Add"), new RequestedExport("sub", "Example.Math", "Sub")],
            options.Exports);
        Assert.Equal(WasmTarget.Wasm64, options.Platform);
        Assert.Equal(FinalWasmOptimization.O3, options.Optimization);
        Assert.Equal("/src=/_/", options.PathMap);
        Assert.Equal(["Entry.cs", "Math.cs"], options.Sources);
        Assert.Equal("interop.json", options.InteropManifest);
        Assert.Equal("layout.json", options.RuntimeLayout);
        Assert.Equal("metadata.json", options.CompilerMetadata);
        Assert.Equal("exceptions.json", options.ExceptionTypeMap);
        Assert.Equal("symbols.json", options.StackTraceSymbols);
        Assert.Equal("callbacks.o", options.CallbackObject);
        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("example:test/test", options.World);
        Assert.Equal(["host_ui", "host_gl"], options.ImportModules);
        Assert.Equal("trace.json", options.DiagnosticTrace);
        Assert.Equal("compiler.jsonl", options.DiagnosticLog);
        Assert.True(options.UseJavaScriptExportBoundary);
        Assert.True(options.StructuredDiagnostics);
        Assert.Equal("/project", options.ProjectDirectory);
        Assert.Equal("/project/obj", options.IntermediateDirectory);
        Assert.Equal("node", options.WasmTools!.Executable);
        Assert.Equal(
            ["--disable-warning=ExperimentalWarning", "run.mjs", "wasm-tools.wasm"],
            options.WasmTools.ArgumentPrefix);
        Assert.Equal("Disabled", options.ManagedLoopInvariantFieldHoisting);
        Assert.Equal("Enabled", options.ManagedStaticDataViewElimination);
        Assert.Equal("Enabled", options.ManagedAddressTakenValuePromotion);
        Assert.Equal("Disabled", options.ManagedMethodInlining);
        Assert.Equal("Disabled", options.ManagedBranchPrefixDuplication);
        Assert.Equal("Enabled", options.ManagedScalarCallPreservation);
        Assert.Equal("Disabled", options.ManagedArrayBoundsCheckElimination);
        Assert.Equal("Enabled", options.ManagedLoopUnrolling);
        Assert.Equal("Enabled", options.ManagedConstantArgumentPropagation);
        Assert.Equal("Disabled", options.ManagedStaticArrayPopulationElimination);
        Assert.Equal("Enabled", options.ManagedConstantCallFolding);
        Assert.Equal("Disabled", options.ManagedSwitchBitTests);
        Assert.Equal("Enabled", options.ManagedDevirtualization);
        Assert.Equal("Disabled", options.ManagedAllocationElimination);
        Assert.Equal("Disabled", options.ManagedLoopRotation);
        Assert.Equal("Enabled", options.ManagedArrayStoreCheckElimination);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("library")]
    public void TargetsWithoutAnEntryMethod(string target)
    {
        var options = Parse("App.dll", "-out:app.wasm", $"-target:{target}");

        Assert.Equal(target == "exe" ? NwcTarget.Exe : NwcTarget.Library, options.Target);
        Assert.Empty(options.EntryType);
        Assert.Empty(options.EntryMethod);
    }

    [Theory]
    [InlineData("None", FinalWasmOptimization.None)]
    [InlineData("none", FinalWasmOptimization.None)]
    [InlineData("O0", FinalWasmOptimization.O0)]
    [InlineData("O1", FinalWasmOptimization.O1)]
    [InlineData("O2", FinalWasmOptimization.O2)]
    [InlineData("O3", FinalWasmOptimization.O3)]
    [InlineData("Os", FinalWasmOptimization.Os)]
    [InlineData("Oz", FinalWasmOptimization.Oz)]
    public void ReadsEveryOptimizationLevel(string level, FinalWasmOptimization expected) =>
        Assert.Equal(expected, Parse("App.dll", "-out:app.wasm", $"-optimize:{level}").Optimization);

    [Theory]
    [InlineData("-jsexportboundary", true)]
    [InlineData("-jsexportboundary+", true)]
    [InlineData("-jsexportboundary-", false)]
    public void ReadsASwitchAsCscDoes(string spelling, bool expected) =>
        Assert.Equal(expected, Parse("App.dll", "-out:app.wasm", spelling).UseJavaScriptExportBoundary);

    [Fact]
    public void TheLastSwitchWins() =>
        Assert.False(Parse("App.dll", "-out:app.wasm", "-jsexportboundary+", "-jsexportboundary-")
            .UseJavaScriptExportBoundary);

    [Theory]
    [InlineData("no input assembly was specified", "-out:app.wasm")]
    [InlineData("only one input assembly can be compiled: First.dll, Second.dll",
        "First.dll", "Second.dll", "-out:app.wasm")]
    [InlineData("no output file was specified (-out)", "App.dll")]
    [InlineData("option '-out' was specified more than once", "App.dll", "-out:a.wasm", "-out:b.wasm")]
    [InlineData("option '-out' requires a value", "App.dll", "-out")]
    [InlineData("option '-out:' requires a value", "App.dll", "-out:")]
    [InlineData("option '-out+' requires a value", "App.dll", "-out+")]
    [InlineData("option '-out:\"\"' requires a value", "App.dll", "-out:\"\"")]
    [InlineData("option '-r:-' requires a value", "App.dll", "-out:a.wasm", "-r:-")]
    [InlineData("-target must be 'exe', 'library' or 'function'", "App.dll", "-out:a.wasm", "-target:winexe")]
    [InlineData("-target:function requires -entry:Type::Method", "App.dll", "-out:a.wasm", "-target:function")]
    [InlineData("-entry is only valid with -target:function", "App.dll", "-out:a.wasm", "-entry:Type::Run")]
    [InlineData("-entry is only valid with -target:function",
        "App.dll", "-out:a.wasm", "-target:library", "-entry:Type::Run")]
    [InlineData("a method must use 'Namespace.Type::Method' syntax",
        "App.dll", "-out:a.wasm", "-target:function", "-entry:Malformed")]
    [InlineData("a method must use 'Namespace.Type::Method' syntax",
        "App.dll", "-out:a.wasm", "-target:function", "-entry:::Run")]
    [InlineData("a method must use 'Namespace.Type::Method' syntax",
        "App.dll", "-out:a.wasm", "-target:function", "-entry:Type::")]
    [InlineData("-export must use 'name=Namespace.Type::Method' syntax", "App.dll", "-out:a.wasm", "-export:bad")]
    [InlineData("-export must use 'name=Namespace.Type::Method' syntax",
        "App.dll", "-out:a.wasm", "-export:=Type::Run")]
    [InlineData("-export must use 'name=Namespace.Type::Method' syntax", "App.dll", "-out:a.wasm", "-export:name=")]
    [InlineData("a method must use 'Namespace.Type::Method' syntax", "App.dll", "-out:a.wasm", "-export:name=Bad")]
    [InlineData("-platform must be 'wasm32' or 'wasm64'", "App.dll", "-out:a.wasm", "-platform:x64")]
    [InlineData("-optimize must be 'None', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'",
        "App.dll", "-out:a.wasm", "-optimize:O4")]
    [InlineData("option '-jsexportboundary:yes' takes '+' or '-', not a value",
        "App.dll", "-out:a.wasm", "-jsexportboundary:yes")]
    [InlineData("-wasmtoolsnode, -wasmtoolscommand and -wasmtoolsmodule must be given together",
        "App.dll", "-out:a.wasm", "-wasmtoolsnode:node")]
    [InlineData("-wasmtoolsnode, -wasmtoolscommand and -wasmtoolsmodule must be given together",
        "App.dll", "-out:a.wasm", "-wasmtoolsnode:node", "-wasmtoolscommand:run.mjs")]
    [InlineData("-wasmtoolsnode, -wasmtoolscommand and -wasmtoolsmodule must be given together",
        "App.dll", "-out:a.wasm", "-wasmtoolsmodule:wasm-tools.wasm")]
    public void RejectsAnInvalidCommandLine(string message, params string[] arguments)
    {
        var exception = Assert.Throws<CompilerException>(() => Parse(arguments));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Equal(message, exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsMissingArguments() =>
        Assert.Throws<ArgumentNullException>(() => CompileCliOptions.Parse(null!));

    [Fact]
    public void AFlagTakesNoValue()
    {
        var values = new NwcOptionValues(NwcTestArguments.Read(CompileCliOptions.Names, "-nologo:yes"));

        var exception = Assert.Throws<CompilerException>(() => values.Flag("nologo"));

        Assert.Equal("option '-nologo:yes' takes no value", exception.Diagnostic.Message);
    }

    private static CompileCliOptions Parse(params string[] arguments) =>
        CompileCliOptions.Parse(NwcTestArguments.Read(CompileCliOptions.Names, arguments));
}
