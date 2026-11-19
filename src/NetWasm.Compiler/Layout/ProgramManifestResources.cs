// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// Every embedded resource of every assembly the compilation loaded, except two that no program reads at run time:
// the trimmer's ILLink.*.xml descriptors, which .NET's trimmer removes too, and a library's FxResources.<Name>.SR
// string table unless the program reaches the type FxResources.<Name>.SR, which the library's SR reads its messages
// through. With UseSystemResourceKeys the library answers with resource keys, so that type is never reached.
internal sealed class ProgramManifestResources(MetadataCompilationSnapshot metadata, ReachableProgram program) :
    IProgramManifestResources
{
    private const string FrameworkStringsPrefix = "FxResources.";
    private const string FrameworkStringsSuffix = ".SR.resources";

    private readonly MetadataCompilationSnapshot _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));

    public ImmutableDictionary<AssemblyIdentity, ImmutableArray<ManifestResourceData>> KeptResources()
    {
        var kept = ImmutableDictionary.CreateBuilder<AssemblyIdentity, ImmutableArray<ManifestResourceData>>();
        foreach (var assembly in _metadata.Assemblies)
        {
            var reachedTypes = assembly.Types.Values
                .Where(type => _program.Types.Contains(type.Key))
                .Select(type => type.FullName)
                .ToHashSet(StringComparer.Ordinal);
            var resources = assembly.ManifestResources
                .Where(resource => Keeps(resource.Name, reachedTypes))
                .ToImmutableArray();
            if (!resources.IsEmpty)
            {
                kept[assembly.Identity] = resources;
            }
        }
        return kept.ToImmutable();
    }

    private static bool Keeps(string name, HashSet<string> reachedTypes) =>
        !(name.StartsWith("ILLink.", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal)) &&
        !(name.StartsWith(FrameworkStringsPrefix, StringComparison.Ordinal) &&
          name.EndsWith(FrameworkStringsSuffix, StringComparison.Ordinal) &&
          !reachedTypes.Contains(name[..^".resources".Length]));
}
