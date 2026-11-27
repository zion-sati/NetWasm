// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationLoader(
    IManagedAssemblyLoader assemblies,
    IMetadataCompilationFactory compilations,
    ITypeForwarderReader forwarders,
    ILoadedAssemblyNativeStructSourceFactory nativeStructs) : IMetadataCompilationLoader
{
    public IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths,
        ImmutableDictionary<string, string>? referenceAssemblyAliases = null,
        IEnumerable<string>? metadataReferencePaths = null,
        IEnumerable<string>? originalContractReferencePaths = null,
        INativeImportBinder? nativeBinding = null,
        INativeVariadicCallPlanner? nativeVariadics = null,
        GeneratedJavaScriptBinding? javaScriptBinding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(referencePaths);
        referenceAssemblyAliases ??= ImmutableDictionary<string, string>.Empty;
        ArgumentNullException.ThrowIfNull(referenceAssemblyAliases);
        var loaded = new List<ManagedAssembly>();
        var contracts = new List<ManagedAssembly>();
        var paths = referencePaths.ToImmutableArray();
        // Reference views contribute bindings only. A view may share an identity
        // with a runtime assembly, but its definitions and bodies never execute.
        var metadataPaths = metadataReferencePaths?.ToImmutableArray() ?? [];
        var typeForwarders = forwarders.Read(paths.Prepend(entryAssemblyPath), referenceAssemblyAliases, metadataPaths);
        try
        {
            // Assemblies are read and parsed side by side, in their given order; when any
            // fails, the first failure in that order is thrown and the rest are disposed.
            var inputs = new List<string> { entryAssemblyPath };
            inputs.AddRange(paths);
            var parsed = new ManagedAssembly?[inputs.Count];
            var failures = new System.Runtime.ExceptionServices.ExceptionDispatchInfo?[inputs.Count];
            System.Threading.Tasks.Parallel.For(0, inputs.Count, index =>
            {
                try
                {
                    parsed[index] = assemblies.Load(inputs[index], referenceAssemblyAliases, typeForwarders, nativeBinding,
                        nativeVariadics, javaScriptBinding);
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
                if (assembly is null) continue;
                // A satellite resource assembly (one with a culture, as each of a localized project's
                // cs-CZ/App.resources.dll and de-DE/App.resources.dll) is no program assembly: NetWasm's
                // globalization is invariant, so no culture loads it, as the SDK leaves a package's satellites out.
                // Satellites share their name, so keeping them would read as duplicates.
                if (assembly.Culture.Length != 0 && !ReferenceEquals(assembly, parsed[0]))
                {
                    assembly.Dispose();
                    continue;
                }
                loaded.Add(assembly);
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

            var assembliesByName = byName.ToImmutable();
            // A struct a P/Invoke or function pointer names may come from another assembly, as on desktop .NET.
            var structs = nativeStructs.Create(assembliesByName);
            foreach (var assembly in loaded)
                assembly.LayCrossAssemblyStructStubs(structs);
            var contractNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contractPath in originalContractReferencePaths ?? [])
            {
                var contract = assemblies.Load(contractPath, referenceAssemblyAliases, typeForwarders);
                contracts.Add(contract);
                if (!contractNames.Add(contract.Identity.Name))
                    throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.DuplicateAssembly,
                        $"duplicate original contract assembly identity '{contract.Identity.Name}'"));
            }

            // An AssemblyRef may belong solely to unreachable library code.
            // Resolvers validate required members/types during reachability;
            // metadata loading still validates image shape and duplicate inputs.
            return compilations.Create(
                entry,
                assembliesByName,
                referenceAssemblyAliases,
                contracts.ToImmutableArray());
        }
        catch
        {
            foreach (var assembly in loaded.Concat(contracts))
            {
                assembly.Dispose();
            }
            throw;
        }
    }

}
