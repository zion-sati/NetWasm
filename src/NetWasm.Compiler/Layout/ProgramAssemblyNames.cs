// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// Each program assembly's display name, from its definition.
internal sealed class ProgramAssemblyNames(MetadataCompilationSnapshot metadata) : IProgramAssemblyNames
{
    private readonly MetadataCompilationSnapshot _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

    public string DisplayName(AssemblyIdentity assembly)
    {
        var loaded = _metadata.Assemblies.FirstOrDefault(candidate => candidate.Identity == assembly) ??
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract, $"the program assembly '{assembly.Name}' was not loaded"));
        return AssemblyIdentityFormatter.DisplayName(assembly, loaded.Reader);
    }
}
