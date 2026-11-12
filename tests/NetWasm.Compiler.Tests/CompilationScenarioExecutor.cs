using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
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
    // The method that reads the outcome of asynchronous work the entry starts. The fixture then
    // runs as an asynchronous Main (AsyncObservationEntry) whose callbacks run that work.
    internal string? ObserveMethodName { get; init; }
    internal bool EmitStackTrace { get; init; }
    internal bool EmitTypeNames { get; init; }
    internal bool EmitPortablePdb { get; init; }
    internal FinalWasmOptimization Optimization { get; init; } =
        TestOptimization.Default;
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
        var (source, entryTypeName, entryMethodName) = scenario.ObserveMethodName is { } observe
            ? Observed(scenario, observe)
            : (scenario.Source, scenario.EntryTypeName, scenario.EntryMethodName);
        var assembly = scenario.EmitPortablePdb
            ? _assets.CompileSourceWithPortablePdb(
                scenario.AssemblyName,
                source,
                scenario.Optimize,
                references)
            : (scenario.Optimize, scenario.AllowUnsafe) switch
            {
                (true, true) => _assets.CompileOptimizedUnsafeSource(
                    scenario.AssemblyName,
                    source,
                    references),
                (true, false) => _assets.CompileOptimizedSource(
                    scenario.AssemblyName,
                    source,
                    references),
                (false, true) => _assets.CompileUnsafeSource(
                    scenario.AssemblyName,
                    source,
                    references),
                (false, false) => _assets.CompileSource(
                    scenario.AssemblyName,
                    source,
                    references),
            };
        var compilerReferences = scenario.ReferencePaths.Add(_assets.CoreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            compilerReferences,
            entryTypeName,
            entryMethodName,
            scenario.ObserveMethodName is null ? scenario.Exports : [],
            scenario.Target,
            DiagnosticTracePath: scenario.DiagnosticTracePath,
            DiagnosticLogPath: scenario.DiagnosticLogPath,
            WitPath: scenario.WitPath,
            WitWorld: scenario.WitWorld,
            EmitTypeNames: scenario.EmitTypeNames,
            EmitStackTrace: scenario.EmitStackTrace,
            ProjectDirectory: _assets.Directory,
            Optimization: scenario.Optimization,
            EntryPointKind: scenario.ObserveMethodName is null
                ? CompilerEntryPointKind.RawFunction
                : CompilerEntryPointKind.ManagedExecutable));

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
                ? compilation.MethodSymbols?.Bytes
                : null,
            callbackEntry: scenario.ObserveMethodName is not null,
            expectedEnvironmentReads: scenario.ExpectedEnvironmentReads,
            expectedPreopenReads: scenario.ExpectedPreopenReads,
            entryInvocationCount: scenario.EntryInvocationCount,
            inputArgument: scenario.ObserveMethodName is not null,
            typeSymbols: scenario.LoadStackTraceSymbols ? compilation.TypeSymbols?.Bytes : null);
    }

    private static (string Source, string EntryTypeName, string EntryMethodName) Observed(
        CompilationScenario scenario,
        string observeMethodName)
    {
        if (!scenario.Exports.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An observed scenario exports only its asynchronous Main.", nameof(scenario));
        }
        var (source, entryTypeName) = AsyncObservationEntry.Append(
            scenario.Source, scenario.EntryTypeName, scenario.EntryMethodName, observeMethodName);
        return (source, entryTypeName, AsyncObservationEntry.MethodName);
    }
}
