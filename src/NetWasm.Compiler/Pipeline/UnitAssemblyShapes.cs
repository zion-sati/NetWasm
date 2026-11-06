using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Pipeline;

/// <summary>
/// What a unit depends on of the program's assemblies: each assembly's shape together with
/// those of everything it references, transitively, and of the core assemblies (those that
/// reference none of the program's) every program builds on.
/// </summary>
internal static class UnitAssemblyShapes
{
    public static FrozenDictionary<string, UnitFingerprint> Of(
        IReadOnlyDictionary<string, ImmutableArray<string>> references,
        IReadOnlyDictionary<string, string>? aliases,
        IReadOnlyDictionary<string, InputFingerprint> shapes)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(shapes);
        string Resolve(string reference) => aliases?.TryGetValue(reference, out var target) == true ? target : reference;
        var cores = references.Where(assembly => !assembly.Value.Any(reference => references.ContainsKey(Resolve(reference))))
            .Select(assembly => assembly.Key)
            .ToArray();
        var result = new Dictionary<string, UnitFingerprint>(references.Count, StringComparer.Ordinal);
        foreach (var name in references.Keys)
        {
            var closure = new SortedSet<string>(cores, StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(name);
            while (pending.TryPop(out var next))
            {
                if (!closure.Add(next) && next != name) continue;
                foreach (var reference in references[next])
                {
                    var resolved = Resolve(reference);
                    if (references.ContainsKey(resolved) && !closure.Contains(resolved)) pending.Push(resolved);
                }
            }
            var fingerprint = new UnitFingerprintBuilder();
            fingerprint.Add(closure.Count);
            foreach (var member in closure)
            {
                fingerprint.Add(member);
                var shape = shapes.GetValueOrDefault(member);
                fingerprint.Add((long)shape.First);
                fingerprint.Add((long)shape.Second);
            }
            result[name] = fingerprint.Finish();
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
