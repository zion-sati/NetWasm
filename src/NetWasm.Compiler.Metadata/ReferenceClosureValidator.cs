using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class ReferenceClosureValidator : IReferenceClosureValidator
{
    private static readonly ImmutableHashSet<string> ForbiddenTargetReferences =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "mscorlib",
            "System.Private.CoreLib",
            "System.Runtime");

    public void Validate(
        IEnumerable<AssemblyReferenceClosure> assemblies,
        IEnumerable<string> availableNames,
        ImmutableDictionary<string, string> aliases)
    {
        var available = availableNames.ToImmutableHashSet(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            foreach (var reference in assembly.References)
            {
                if (ForbiddenTargetReferences.Contains(reference) &&
                    !aliases.ContainsKey(reference))
                {
                    throw new CompilerException(
                        new CompilerDiagnostic(
                            DiagnosticCode.AssemblyResolution,
                            $"target assembly '{assembly.Identity.Name}' references " +
                            $"forbidden desktop assembly '{reference}'"));
                }
                var resolvedReference = aliases.TryGetValue(reference, out var alias)
                    ? alias
                    : reference;
                if (!available.Contains(resolvedReference))
                {
                    throw new CompilerException(
                        new CompilerDiagnostic(
                            DiagnosticCode.AssemblyResolution,
                            $"target assembly '{assembly.Identity.Name}' has " +
                            $"unresolved reference '{reference}'"));
                }
            }
        }
    }
}
