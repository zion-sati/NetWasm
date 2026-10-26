using NetWasm.Compiler.Cli;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompileCliOptionsTests
{
    [Fact]
    public void LibraryCompilationRequiresNoEntryMethod()
    {
        var options = CompileCliOptions.Parse([
            "--input", "library.dll", "--output", "library.wasm",
            "--entry-kind", "library", "--export", "add=Example.Worker::Add"]);

        Assert.Equal(CompilerEntryPointKind.Library, options.EntryPointKind);
        Assert.Empty(options.EntryType);
        Assert.Empty(options.EntryMethod);
        Assert.Equal("add", Assert.Single(options.Exports).Name);
    }

    [Theory]
    [InlineData("raw-function", CompilerEntryPointKind.RawFunction)]
    [InlineData("managed-executable", CompilerEntryPointKind.ManagedExecutable)]
    public void ParsesExplicitEntryKinds(string spelling, CompilerEntryPointKind expected)
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry-kind", spelling, "--entry", "Example.Entry::Run"]);

        Assert.Equal(expected, options.EntryPointKind);
        Assert.Equal("Example.Entry", options.EntryType);
        Assert.Equal("Run", options.EntryMethod);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("unknown")]
    public void RejectsConflictingLibraryEntryAndUnknownKinds(string kind)
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry", "Example.Entry::Run", "--entry-kind", kind]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Contains(kind == "library" ? "cannot specify" : "entry kind must be", exception.Message);
    }

    [Fact]
    public void ParsesTargetReferencesExportsAndComponentSelection()
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm64",
            "--reference", "first.dll",
            "--reference", "second.dll",
            "--export", "other=Example.Entry::Other",
            "--wit", "contract.wit",
            "--world", "example:test/test",
            "--diagnostic-trace", "trace.json",
            "--diagnostic-log", "compiler.jsonl",
            "--javascript-export-boundary", "true",
            "--source", "Entry.cs",
            "--source", "Helpers.cs",
            "--import-module", "host_ui",
            "--import-module", "host_gl",
        ]);

        Assert.Equal(WasmTarget.Wasm64, options.Target);
        Assert.Equal(["host_ui", "host_gl"], options.ImportModules.ToArray());
        Assert.Equal(["first.dll", "second.dll"], options.References);
        var export = Assert.Single(options.Exports);
        Assert.Equal("other", export.Name);
        Assert.Equal("Example.Entry", export.TypeName);
        Assert.Equal("Other", export.MethodName);
        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("trace.json", options.DiagnosticTrace);
        Assert.Equal("compiler.jsonl", options.DiagnosticLog);
        Assert.Equal(["Entry.cs", "Helpers.cs"], options.Sources);
        Assert.True(options.UseJavaScriptExportBoundary);
    }

    [Theory]
    [InlineData("none", FinalWasmOptimization.None)]
    [InlineData("None", FinalWasmOptimization.None)]
    [InlineData("O0", FinalWasmOptimization.O0)]
    [InlineData("O1", FinalWasmOptimization.O1)]
    [InlineData("O2", FinalWasmOptimization.O2)]
    [InlineData("O3", FinalWasmOptimization.O3)]
    [InlineData("Os", FinalWasmOptimization.Os)]
    [InlineData("Oz", FinalWasmOptimization.Oz)]
    public void ParsesCompilerOptimizationPresetAndIndividualSelections(
        string optimization,
        FinalWasmOptimization expected)
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--optimization", optimization,
            "--managed-loop-invariant-field-hoisting", "Disabled",
            "--managed-static-data-view-elimination", "Enabled",
            "--managed-address-taken-value-promotion", "Enabled",
            "--managed-method-inlining", "Disabled",
            "--managed-branch-prefix-duplication", "Disabled",
            "--managed-scalar-call-preservation", "Enabled",
            "--managed-array-bounds-check-elimination", "Disabled",
            "--managed-loop-unrolling", "Enabled",
            "--managed-constant-argument-propagation", "Enabled",
            "--managed-static-array-population-elimination", "Disabled",
            "--managed-constant-call-folding", "Enabled",
            "--managed-switch-bit-tests", "Disabled",
            "--managed-devirtualization", "Enabled",
            "--managed-allocation-elimination", "Disabled",
            "--managed-loop-rotation", "Disabled",
            "--managed-array-store-check-elimination", "Enabled",
        ]);

        Assert.Equal(expected, options.Optimization);
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

    [Fact]
    public void RejectsUnknownCompilerOptimizationPreset()
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--optimization", "O4",
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Contains("optimization must be", exception.Message);
    }

    [Theory]
    [InlineData("--managed-address-taken-value-promotion")]
    [InlineData("--managed-method-inlining")]
    [InlineData("--managed-array-bounds-check-elimination")]
    [InlineData("--managed-loop-unrolling")]
    [InlineData("--managed-constant-argument-propagation")]
    [InlineData("--managed-static-array-population-elimination")]
    [InlineData("--managed-constant-call-folding")]
    [InlineData("--managed-switch-bit-tests")]
    [InlineData("--managed-devirtualization")]
    [InlineData("--managed-allocation-elimination")]
    [InlineData("--managed-loop-rotation")]
    [InlineData("--managed-array-store-check-elimination")]
    public void RejectsRepeatedPromotionAndInliningSelections(string option)
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            option, "Auto",
            option, "Enabled",
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Contains(option, exception.Message);
        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void DefaultsCompilerOptimizationToOzAndRejectsDuplicateSelections()
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
        ]);

        Assert.Equal(FinalWasmOptimization.Oz, options.Optimization);
        Assert.Null(options.ManagedAddressTakenValuePromotion);
        Assert.Null(options.ManagedMethodInlining);
        Assert.Null(options.ManagedScalarCallPreservation);
        Assert.Null(options.ManagedArrayBoundsCheckElimination);
        Assert.Null(options.ManagedLoopUnrolling);
        Assert.Null(options.ManagedConstantArgumentPropagation);
        Assert.Null(options.ManagedStaticArrayPopulationElimination);
        Assert.Null(options.ManagedConstantCallFolding);
        Assert.Null(options.ManagedSwitchBitTests);
        Assert.Null(options.ManagedDevirtualization);
        Assert.Null(options.ManagedAllocationElimination);
        Assert.Null(options.ManagedLoopRotation);
        Assert.Null(options.ManagedArrayStoreCheckElimination);
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--managed-scalar-call-preservation", "Auto",
            "--managed-scalar-call-preservation", "Enabled",
        ]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--managed-static-data-view-elimination", "Auto",
            "--managed-static-data-view-elimination", "Enabled",
        ]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--managed-branch-prefix-duplication", "Auto",
            "--managed-branch-prefix-duplication", "Enabled",
        ]));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("name=")]
    public void RejectsMalformedExports(string export)
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--export", export,
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMalformedEntryAndUnsupportedTarget()
    {
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Malformed"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm128"]));
    }

    [Fact]
    public void RejectsUnsupportedJavaScriptExportBoundaryValue()
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--javascript-export-boundary", "maybe",
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
        Assert.Contains("must be 'true' or 'false'", exception.Message);
    }

    [Fact]
    public void RejectsUnknownOptionMissingValueAndRequiredFields()
    {
        Assert.Throws<ArgumentNullException>(() => CompileCliOptions.Parse(null!));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse(["--unknown", "value"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse(["--input"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--output", "application.wasm", "--entry", "Example.Entry::Run"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--entry", "Example.Entry::Run"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry", "Example.Entry::Run", "--input", "duplicate.dll"]));
    }

    [Fact]
    public void OptionReaderRejectsNullCallbacksAndTrailingOptions()
    {
        Assert.Throws<ArgumentNullException>(() => CliOptionReader.Read(null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => CliOptionReader.Read([], null!));
        Assert.Throws<CompilerException>(() => CliOptionReader.Read(["--input"], (_, _) => { }));
    }
}
