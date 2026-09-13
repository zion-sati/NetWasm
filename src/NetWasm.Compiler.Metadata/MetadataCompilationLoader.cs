using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationLoader(
    IManagedAssemblyLoader assemblies,
    IReferenceClosureValidator referenceClosure,
    IMetadataCompilationFactory compilations) : IMetadataCompilationLoader
{
    public IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths,
        ImmutableDictionary<string, string>? referenceAssemblyAliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(referencePaths);
        referenceAssemblyAliases ??= ImmutableDictionary<string, string>.Empty;
        ArgumentNullException.ThrowIfNull(referenceAssemblyAliases);
        var loaded = new List<ManagedAssembly>();
        try
        {
            var entry = assemblies.Load(entryAssemblyPath, referenceAssemblyAliases);
            loaded.Add(entry);
            foreach (var referencePath in referencePaths)
            {
                loaded.Add(assemblies.Load(referencePath, referenceAssemblyAliases));
            }

            var byName = ImmutableDictionary.CreateBuilder<string, ManagedAssembly>(
                StringComparer.Ordinal);
            foreach (var assembly in loaded)
            {
                if (!byName.TryAdd(assembly.Identity.Name, assembly))
                {
                    throw new CompilerException(
                        new CompilerDiagnostic(
                            DiagnosticCode.DuplicateAssembly,
                            $"duplicate assembly identity '{assembly.Identity.Name}'"));
                }
            }

            referenceClosure.Validate(
                byName.Values.Select(assembly => new AssemblyReferenceClosure(
                    assembly.Identity,
                    assembly.References)),
                byName.Keys,
                referenceAssemblyAliases);
            return compilations.Create(
                entry,
                byName.ToImmutable(),
                referenceAssemblyAliases);
        }
        catch
        {
            foreach (var assembly in loaded)
            {
                assembly.Dispose();
            }
            throw;
        }
    }

}
