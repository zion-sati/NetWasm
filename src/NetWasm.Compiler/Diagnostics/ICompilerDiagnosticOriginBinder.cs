// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed record CompilationAssemblySource(AssemblyIdentity Identity, string Path);

internal interface ICompilerDiagnosticOriginBinder
{
    CompilerDiagnostic Bind(CompilerDiagnostic diagnostic,
        ImmutableArray<CompilationAssemblySource> assemblies,
        ImmutableArray<CompilationReferenceOrigin> origins);
}
