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
        Assert.Null(options.CompiledUnits);
        Assert.False(options.VerifyCompiledUnits);
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
            "-metadatareference:view.dll,other-view.dll", "-originalcontract:contract.dll",
            "-referenceorigin:first.dll;package=Example.Library;version=13.0.3;asset=lib/netstandard2.0/first.dll",
            "-referenceorigin:view.dll;original=contract.dll",
            "-export:add=Example.Math::Add", "-export:sub=Example.Math::Sub", "-platform:wasm64",
            "-optimize:O3", "-pathmap:/src=/_/", "-source:Entry.cs", "-source:Math.cs",
            "-interopmanifest:interop.json", "-runtimelayout:layout.json", "-compilermetadata:metadata.json",
            "-typesymbols:app.wasm.type-symbols", "-methodsymbols:app.wasm.method-symbols", "-typenames+",
            "-callbackobject:callbacks.o", "-wit:contract.wit", "-world:example:test/test",
            "-importmodule:host_ui", "-importmodule:host_gl", "-diagnostictrace:trace.json",
            "-diagnosticlog:compiler.jsonl", "-jsexportboundary+", "-structureddiagnostics",
            "-compiledunits-", "-verifycompiledunits+",
            "-projectdirectory:/project", "-intermediatedirectory:/project/obj",
            "-wasmtoolsnode:node", "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm",
            "-managedloopinvariantfieldhoisting:Disabled", "-managedstaticdataviewelimination:Enabled",
            "-managedaddresstakenvaluepromotion:Enabled", "-managedmethodinlining:Disabled",
            "-managedbranchprefixduplication:Disabled", "-managedscalarcallpreservation:Enabled",
            "-managedarrayboundscheckelimination:Disabled", "-managedloopunrolling:Enabled",
            "-managedconstantargumentpropagation:Enabled", "-managedstaticarraypopulationelimination:Disabled",
            "-managedconstantcallfolding:Enabled", "-managedswitchbittests:Disabled",
            "-manageddevirtualization:Enabled", "-managedallocationelimination:Disabled",
            "-managedlooprotation:Disabled", "-managedarraystorecheckelimination:Enabled",
            "-manageddispatcherelimination:Disabled",
            "-managedemptyfinallyremoval:Enabled",
            "-managedstructscalarreplacement:Disabled",
            "-managedallocationscalarreplacement:Enabled",
            "-managedidempotentloopcollapse:Disabled",
            "-managedloopstoreforwarding:Enabled",
            "-managedarrayloopidioms:Disabled",
            "-managedloopvectorization:Disabled",
            "-managedpurecallreuse:Disabled",
            "-managedfieldloadreuse:Disabled",
            "-managedconditionalstoremerging:Disabled",
            "-managedinlinearraydata:Disabled",
            "-managedquotientreuse:Disabled",
            "-managedintegercheckelimination:Disabled",
            "-managedloopinvariantexpressionhoisting:Disabled",
            "-managedmulticastinlinedispatch:Enabled",
            "-managednewobjectnullcheckelimination:Disabled",
            "-managedloopstrengthreduction:Disabled",
            "-managedrangebranchfolding:Enabled",
            "-managedunwrittenstaticfieldfolding:Disabled");

        Assert.Equal(NwcTarget.Function, options.Target);
        Assert.Equal("Example.Entry", options.EntryType);
        Assert.Equal("Run", options.EntryMethod);
        Assert.Equal(["first.dll", "second.dll", "third.dll", "fourth.dll", "fifth.dll"], options.References);
        Assert.Equal(["view.dll", "other-view.dll"], options.MetadataReferences);
        Assert.Equal(["contract.dll"], options.OriginalContracts);
        Assert.Equal(
            [
                new CompilationReferenceOrigin("first.dll", CompilationReferenceRole.Implementation,
                    "Example.Library", "13.0.3", "lib/netstandard2.0/first.dll"),
                new CompilationReferenceOrigin("view.dll", CompilationReferenceRole.Metadata, OriginalReferencePath: "contract.dll"),
            ],
            options.ReferenceOrigins);
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
        Assert.Equal("app.wasm.type-symbols", options.TypeSymbols);
        Assert.Equal("app.wasm.method-symbols", options.MethodSymbols);
        Assert.True(options.TypeNames);
        Assert.Equal("callbacks.o", options.CallbackObject);
        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("example:test/test", options.World);
        Assert.Equal(["host_ui", "host_gl"], options.ImportModules);
        Assert.Equal("trace.json", options.DiagnosticTrace);
        Assert.Equal("compiler.jsonl", options.DiagnosticLog);
        Assert.True(options.UseJavaScriptExportBoundary);
        Assert.True(options.StructuredDiagnostics);
        Assert.False(options.CompiledUnits);
        Assert.True(options.VerifyCompiledUnits);
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
        Assert.Equal("Disabled", options.ManagedDispatcherElimination);
        Assert.Equal("Enabled", options.ManagedEmptyFinallyRemoval);
        Assert.Equal("Disabled", options.ManagedStructScalarReplacement);
        Assert.Equal("Enabled", options.ManagedAllocationScalarReplacement);
        Assert.Equal("Disabled", options.ManagedIdempotentLoopCollapse);
        Assert.Equal("Enabled", options.ManagedLoopStoreForwarding);
        Assert.Equal("Disabled", options.ManagedArrayLoopIdioms);
        Assert.Equal("Disabled", options.ManagedLoopVectorization);
        Assert.Equal("Disabled", options.ManagedPureCallReuse);
        Assert.Equal("Disabled", options.ManagedFieldLoadReuse);
        Assert.Equal("Disabled", options.ManagedConditionalStoreMerging);
        Assert.Equal("Disabled", options.ManagedInlineArrayData);
        Assert.Equal("Disabled", options.ManagedQuotientReuse);
        Assert.Equal("Disabled", options.ManagedIntegerCheckElimination);
        Assert.Equal("Disabled", options.ManagedLoopInvariantExpressionHoisting);
        Assert.Equal("Enabled", options.ManagedMulticastInlineDispatch);
        Assert.Equal("Disabled", options.ManagedNewObjectNullCheckElimination);
        Assert.Equal("Disabled", options.ManagedLoopStrengthReduction);
        Assert.Equal("Enabled", options.ManagedRangeBranchFolding);
        Assert.Equal("Disabled", options.ManagedUnwrittenStaticFieldFolding);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("library")]
    [InlineData("coremodule")]
    public void TargetsWithoutAnEntryMethod(string target)
    {
        var options = Parse("App.dll", "-out:app.wasm", $"-target:{target}");

        Assert.Equal(target switch
        {
            "exe" => NwcTarget.Exe,
            "library" => NwcTarget.Library,
            _ => NwcTarget.CoreModule,
        }, options.Target);
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
    [InlineData("-target must be 'exe', 'library', 'function' or 'coremodule'", "App.dll", "-out:a.wasm", "-target:winexe")]
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
    [InlineData("-referenceorigin must start with a reference file", "App.dll", "-out:a.wasm", "-referenceorigin:;")]
    [InlineData("-referenceorigin names 'other.dll', which is not a -reference or -metadatareference",
        "App.dll", "-out:a.wasm", "-referenceorigin:other.dll")]
    [InlineData("-referenceorigin facts are package=, version=, asset= and original=, not 'bogus=1'",
        "App.dll", "-out:a.wasm", "-r:first.dll", "-referenceorigin:first.dll;bogus=1")]
    [InlineData("-wasmtoolsnode, -wasmtoolscommand and -wasmtoolsmodule must be given together",
        "App.dll", "-out:a.wasm", "-wasmtoolsmodule:wasm-tools.wasm")]
    public void RejectsAnInvalidCommandLine(string message, params string[] arguments)
    {
        var exception = Assert.Throws<CompilerException>(() => Parse(arguments));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Equal(message, exception.Diagnostic.Message);
    }

    // A fact without a value leaves that fact unset.
    [Fact]
    public void AReferenceOriginFactWithoutAValueIsUnset()
    {
        var origin = Assert.Single(Parse("App.dll", "-out:a.wasm", "-r:first.dll", "-referenceorigin:first.dll;package")
            .ReferenceOrigins);

        Assert.Equal("first.dll", origin.Path);
        Assert.Null(origin.PackageId);
    }

    [Fact]
    public void RunsANamedNativeWasmToolsInPlaceOfTheWasiOne()
    {
        var alone = Parse(["App.dll", "-out:a.wasm", "-wasmtools:/tools/wasm-tools"]);
        var withWasi = Parse(["App.dll", "-out:a.wasm", "-wasmtools:/tools/wasm-tools",
            "-wasmtoolsnode:node", "-wasmtoolscommand:run.mjs", "-wasmtoolsmodule:wasm-tools.wasm"]);

        Assert.Equal("/tools/wasm-tools", alone.WasmTools!.Executable);
        Assert.Empty(alone.WasmTools.ArgumentPrefix);
        Assert.Equal("/tools/wasm-tools", withWasi.WasmTools!.Executable);
        Assert.Empty(withWasi.WasmTools.ArgumentPrefix);
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

    // The environment overrides the switches for a process; without it, units serve the
    // builds that compile for speed (O0 and None) unless a switch says otherwise.
    [Theory]
    [InlineData("0", "-optimize:O0", false)]
    [InlineData("1", "-optimize:O3", true)]
    [InlineData("verify", "-optimize:O3", true)]
    [InlineData(null, "-optimize:O0", true)]
    [InlineData(null, "-optimize:None", true)]
    [InlineData(null, "-optimize:O3", false)]
    [InlineData(null, "-compiledunits+", true)]
    [InlineData(null, "-verifycompiledunits+", true)]
    public void CompiledUnitsFollowTheEnvironmentThenTheSwitchesThenTheLevel(string? environment, string option, bool units)
    {
        var options = option.StartsWith("-optimize", StringComparison.Ordinal)
            ? Parse("App.dll", "-out:app.wasm", option)
            : Parse("App.dll", "-out:app.wasm", "-optimize:O3", option);

        Assert.Equal(units, CompileCliCommand.CompiledUnits(environment, options));
    }

    [Theory]
    [InlineData(null, "-optimize:O0", false)]
    [InlineData("1", "-optimize:O0", false)]
    [InlineData("verify", "-optimize:O3", true)]
    [InlineData(null, "-verifycompiledunits+", true)]
    public void UnitsAreVerifiedWhereTheEnvironmentOrTheSwitchesAsk(string? environment, string option, bool verified) =>
        Assert.Equal(verified, CompileCliCommand.VerifiesCompiledUnits(environment, Parse("App.dll", "-out:app.wasm", option)));

    [Fact]
    public void ReadsFeatureSwitchesAndLinkedNativeLibraries()
    {
        var options = Parse("App.dll", "-out:app.wasm", "-featureswitch:Example.A=true", "-featureswitch:Example.B=False",
            "-featureswitch:Example.A=false", "-linkednativeonly+", "-nativelibrary:libz", "-nativelibrary:libgpu",
            "-runtimeproperty:Example.Z=a=b", "-runtimeproperty:Example.A=", "-runtimeproperty:Example.Z=last");

        Assert.Equal(
            new Dictionary<string, bool> { ["Example.A"] = false, ["Example.B"] = false },
            options.FeatureSwitches);
        Assert.True(options.LinkedNativeOnly);
        Assert.Equal(["libz", "libgpu"], options.NativeLibraries);
        Assert.Equal([("Example.A", ""), ("Example.Z", "last")], options.RuntimeProperties);
        Assert.Equal("a=b", Parse("App.dll", "-out:app.wasm", "-runtimeproperty:Example.Z=a=b").RuntimeProperties[0].Value);
    }

    [Fact]
    public void RejectsARuntimePropertyWithoutAName()
    {
        var exception = Assert.Throws<CompilerException>(() => Parse("App.dll", "-out:app.wasm", "-runtimeproperty:=value"));

        Assert.Equal("-runtimeproperty '=value' must be name=value", exception.Diagnostic.Message);
    }

    [Theory]
    [InlineData("-featureswitch:Example.A=yes")]
    [InlineData("-featureswitch:=true")]
    [InlineData("-featureswitch:Example.A")]
    public void RejectsAFeatureSwitchWithoutANameAndBooleanValue(string argument)
    {
        var exception = Assert.Throws<CompilerException>(() => Parse("App.dll", "-out:app.wasm", argument));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.StartsWith("-featureswitch '", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static CompileCliOptions Parse(params string[] arguments) =>
        CompileCliOptions.Parse(NwcTestArguments.Read(CompileCliOptions.Names, arguments));
}
