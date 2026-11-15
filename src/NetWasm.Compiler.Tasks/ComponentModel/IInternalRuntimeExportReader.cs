// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IInternalRuntimeExportReader
{
    ImmutableArray<WasmInternalExport> Read(string metadata);
}
