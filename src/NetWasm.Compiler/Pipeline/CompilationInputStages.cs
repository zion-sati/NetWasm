using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.Inputs;

namespace NetWasm.Compiler.Pipeline;

/// <summary>Where a compile keeps its compiled units and inputs, when it keeps them.</summary>
internal static class CompiledUnitPolicy
{
    // Units serve every level; the hosts turn them on by default at O0 only.
    public static string? DirectoryFor(CompilerOptions options) =>
        options.CompiledUnits && options.DiagnosticTracePath is null
            ? options.IntermediateOutputPath
            : null;

}

/// <summary>This compile's inputs and how they differ from the last compile's.</summary>
internal sealed record CompilationInputObservation(
    string Directory,
    CompilationInputs Previous,
    CompilationInputs Current,
    CompilationInputChanges Changes)
{
    // A plain account of the changes, kept with the inputs.
    public string Report { get; init; } = "";
    // Whether managed optimizations analyse each method alone: O0's (or no optimization's)
    // defaults run no analysis across methods (inlining, constant arguments, call folding),
    // so a method's code depends on its own body alone.
    public bool MethodLocalAnalysis { get; init; } = true;
}

internal interface ICompilationInputDetector
{
    // Fingerprints the inputs and compares them with the last compile's; null when the
    // compile keeps no units.
    CompilationInputObservation? Detect(CompilerOptions options, MetadataCompilationSnapshot metadata);
}

internal interface ICompilationInputRecorder
{
    // Keeps a successful compile's inputs and its methods' contributions for the next
    // compile to compare with, and reports whether the edited methods kept theirs.
    void Record(CompilationInputObservation? observation, ReachableProgram program);

    // Keeps the inputs of a compile the early cutoff served from the last one, whose
    // methods each contributed what they did then.
    void Record(CompilationInputObservation observation);
}

internal sealed class CompilationInputDetector(
    ICompilationInputReader inputs,
    NetWasm.Compiler.Wasm.Units.IUnitInputsLoader loader) : ICompilationInputDetector
{
    public CompilationInputObservation? Detect(CompilerOptions options, MetadataCompilationSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        if (CompiledUnitPolicy.DirectoryFor(options) is not { } directory)
            return null;
        var started = Stopwatch.GetTimestamp();
        var previous = loader.Load(directory) is { } kept
            ? CompilationInputsFormat.Read(kept)
            : CompilationInputs.Empty;
        var current = inputs.Read(metadata, previous);
        var changes = CompilationInputChanges.Between(previous, current);
        var elapsed = Stopwatch.GetElapsedTime(started);
        return new(directory, previous, current, changes)
        {
            MethodLocalAnalysis = options.Optimization is FinalWasmOptimization.O0 or FinalWasmOptimization.None &&
                (options.OptimizationOverrides is null ||
                 options.OptimizationOverrides == new NetWasm.Compiler.ComponentModel.Optimization.WasmOptimizationOverrides()),
            Report = changes.Describe() + "read in " +
                elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms\n",
        };
    }
}

internal sealed class CompilationInputRecorder(
    NetWasm.Compiler.Wasm.Units.IUnitInputsCommitter committer) : ICompilationInputRecorder
{
    public void Record(CompilationInputObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var current = observation.Current with { Contributions = observation.Previous.Contributions };
        committer.Commit(observation.Directory, CompilationInputsFormat.Write(current), observation.Report);
    }

    public void Record(CompilationInputObservation? observation, ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (observation is null)
            return;
        var current = WithContributions(observation.Current, program);
        committer.Commit(observation.Directory, CompilationInputsFormat.Write(current),
            observation.Report + DescribeContributions(observation.Previous, current, observation.Changes) +
            DescribeCutoff(observation, current));
    }

    // The compile's inputs with what each analysed method contributed.
    internal static CompilationInputs WithContributions(CompilationInputs current, ReachableProgram program)
    {
        var contributions = new Dictionary<string, MethodContributionRecord>(
            program.MethodContributions.Count, StringComparer.Ordinal);
        foreach (var (name, contribution) in program.MethodContributions)
        {
            contributions[name] = new(contribution.Definition.Assembly.Name,
                contribution.Definition.MetadataToken, contribution.Fingerprint);
        }
        return current with { Contributions = contributions.ToFrozenDictionary(StringComparer.Ordinal) };
    }

    internal static string DescribeCutoff(CompilationInputObservation observation, CompilationInputs current)
    {
        var (applies, reason) = CutoffVerdict(observation, current);
        return $"early cutoff: {(applies ? "yes" : "no")} ({reason})\n";
    }

    // Why the early cutoff cannot apply whatever the edited methods add, or null: it needs an
    // earlier compile, only method bodies changed, and method-local analysis.
    internal static string? CutoffPreconditions(CompilationInputObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Previous.Assemblies.IsEmpty)
            return "no earlier compile";
        if (!observation.Changes.IsBodiesOnly)
            return "an assembly was added, removed or changed shape";
        if (!observation.MethodLocalAnalysis)
            return "managed optimizations analyse across methods";
        return null;
    }

    // Whether the early cutoff applies: only bodies changed, and every analysed instance of
    // an edited method adds to the program what it added last time, so nothing beyond the
    // edited methods' own code can differ from the last compile's module.
    internal static (bool Applies, string Reason) CutoffVerdict(CompilationInputObservation observation,
        CompilationInputs current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (CutoffPreconditions(observation) is { } unmet)
            return (false, unmet);
        var changes = observation.Changes;
        var edited = new HashSet<(string Assembly, int Token)>();
        foreach (var assembly in changes.Assemblies)
        {
            foreach (var token in assembly.ChangedBodies) edited.Add((assembly.Name, token));
        }
        if (edited.Count == 0)
            return (true, "nothing changed");
        var instances = 0;
        foreach (var (name, contribution) in current.Contributions)
        {
            if (!edited.Contains((contribution.Assembly, contribution.Token))) continue;
            instances++;
            if (!observation.Previous.Contributions.TryGetValue(name, out var before) ||
                before.Fingerprint != contribution.Fingerprint)
            {
                return (false, $"{name} changed what it adds to the program");
            }
        }
        foreach (var (name, contribution) in observation.Previous.Contributions)
        {
            if (edited.Contains((contribution.Assembly, contribution.Token)) && !current.Contributions.ContainsKey(name))
                return (false, $"{name} is no longer analysed");
        }
        return (true, $"{instances} analysed instances of {edited.Count} edited bodies keep their contributions");
    }

    // Whether every analysed instance of an edited method added what it added last time.
    private static string DescribeContributions(
        CompilationInputs previous, CompilationInputs current, CompilationInputChanges changes)
    {
        var edited = new HashSet<(string Assembly, int Token)>();
        foreach (var assembly in changes.Assemblies)
        {
            foreach (var token in assembly.ChangedBodies) edited.Add((assembly.Name, token));
        }
        if (edited.Count == 0 || previous.Contributions.Count == 0)
            return "contributions: no edited methods to compare\n";
        var kept = 0;
        var changed = new List<string>();
        foreach (var (name, contribution) in current.Contributions)
        {
            if (!edited.Contains((contribution.Assembly, contribution.Token))) continue;
            if (previous.Contributions.TryGetValue(name, out var before) && before.Fingerprint == contribution.Fingerprint)
                kept++;
            else
                changed.Add(name);
        }
        foreach (var (name, contribution) in previous.Contributions)
        {
            if (edited.Contains((contribution.Assembly, contribution.Token)) && !current.Contributions.ContainsKey(name))
                changed.Add(name + " (no longer analysed)");
        }
        return $"contributions: {kept} kept, {changed.Count} changed" +
            (changed.Count == 0 ? "" : ": " + string.Join(", ", changed)) + "\n";
    }
}
