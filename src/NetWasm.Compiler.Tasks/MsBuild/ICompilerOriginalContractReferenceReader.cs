// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Build.Framework;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerOriginalContractReferenceReader
{
    ImmutableArray<string> Read(IEnumerable<ITaskItem> references);
}
