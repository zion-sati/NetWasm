// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerReferenceOriginReader
{
    ImmutableArray<CompilationReferenceOrigin> Read(
        ITaskItem[] references, CompilationReferenceRole role);
}
