using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

/// <summary>
/// Describes one managed fixture execution without carrying its assertion.
/// The caller remains responsible for asserting the observable result.
/// </summary>
internal sealed record CompilationScenario(
    string AssemblyName,
    string Source,
    string EntryTypeName,
    bool Optimize,
    WasmTarget Target,
    int Input,
    ImmutableArray<string> ReferencePaths)
{
    internal string EntryMethodName { get; init; } = "Run";
    internal ImmutableArray<RequestedExport> Exports { get; init; } = [];
    internal bool AllowUnsafe { get; init; }
    internal ImmutableDictionary<string, string> Environment { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    internal string? TimeZoneAssetPath { get; init; }
    internal int ExpectedEnvironmentReads { get; init; } = 1;
    internal int ExpectedPreopenReads { get; init; } = 1;
    internal string? WitPath { get; init; }
    internal string? WitWorld { get; init; }
    internal bool DrainReactor { get; init; }
    internal string? ObserveExportName { get; init; }
    internal bool EmitStackTrace { get; init; }
    internal bool LoadStackTraceSymbols { get; init; } = true;
    internal int EntryInvocationCount { get; init; } = 1;
    internal string? DiagnosticTracePath { get; init; }
    internal string? DiagnosticLogPath { get; init; }
}

/// <summary>
/// Test-only Facade for the common source compilation, module validation, and
/// target-aware semantic execution action.
/// </summary>
internal interface ICompilationScenarioExecutor
{
    int Execute(CompilationScenario scenario);
}

internal sealed class CompilationScenarioExecutor(TestAssets assets) :
    ICompilationScenarioExecutor
{
    private readonly TestAssets _assets = assets ??
        throw new ArgumentNullException(nameof(assets));

    int ICompilationScenarioExecutor.Execute(CompilationScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var references = scenario.ReferencePaths.ToArray();
        var assembly = (scenario.Optimize, scenario.AllowUnsafe) switch
        {
            (true, true) => _assets.CompileOptimizedUnsafeSource(
                scenario.AssemblyName,
                scenario.Source,
                references),
            (true, false) => _assets.CompileOptimizedSource(
                scenario.AssemblyName,
                scenario.Source,
                references),
            (false, true) => _assets.CompileUnsafeSource(
                scenario.AssemblyName,
                scenario.Source,
                references),
            (false, false) => _assets.CompileSource(
                scenario.AssemblyName,
                scenario.Source,
                references),
        };
        var compilerReferences = scenario.ReferencePaths.Add(_assets.CoreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            compilerReferences,
            scenario.EntryTypeName,
            scenario.EntryMethodName,
            scenario.Exports,
            scenario.Target,
            DiagnosticTracePath: scenario.DiagnosticTracePath,
            DiagnosticLogPath: scenario.DiagnosticLogPath,
            WitPath: scenario.WitPath,
            WitWorld: scenario.WitWorld,
            EmitStackTrace: scenario.EmitStackTrace));

        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, _assets.Directory);

        return CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            _assets.Directory,
            scenario.Input,
            scenario.Target,
            compilation.StaticDataEnd,
            scenario.Environment,
            scenario.TimeZoneAssetPath,
            scenario.LoadStackTraceSymbols
                ? compilation.StackTraceSymbols?.Bytes
                : null,
            drainReactor: scenario.DrainReactor,
            observeExportName: scenario.ObserveExportName,
            expectedEnvironmentReads: scenario.ExpectedEnvironmentReads,
            expectedPreopenReads: scenario.ExpectedPreopenReads,
            entryInvocationCount: scenario.EntryInvocationCount);
    }
}
