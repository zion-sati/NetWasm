// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.EntryPoints;

public sealed record CompilationEntry(
    MethodDefinitionModel? EntryPoint,
    ImmutableArray<ProgramExport> Exports);
