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
            // Assemblies are read and parsed side by side, in their given order; when any
            // fails, the first failure in that order is thrown and the rest are disposed.
            var paths = new List<string> { entryAssemblyPath };
            paths.AddRange(referencePaths);
            var parsed = new ManagedAssembly?[paths.Count];
            var failures = new System.Runtime.ExceptionServices.ExceptionDispatchInfo?[paths.Count];
            System.Threading.Tasks.Parallel.For(0, paths.Count, index =>
            {
                try
                {
                    parsed[index] = assemblies.Load(paths[index], referenceAssemblyAliases);
                }
#pragma warning disable CA1031 // Rethrown below, in path order.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    failures[index] = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                }
            });
            foreach (var assembly in parsed)
            {
                if (assembly is not null) loaded.Add(assembly);
            }
            Array.Find(failures, failure => failure is not null)?.Throw();
            var entry = parsed[0]!;

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
