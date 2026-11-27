// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Debugging;

/// <summary>
/// Debug information for a linked module (NetWasmDebugInfo): the module keeps its function names, and with the
/// program's method symbols (written when managed stack traces are on) it also carries DWARF line information that
/// maps its code to C# source lines. The source directory is where relative source documents are found.
/// </summary>
public sealed record WasmDebugInformation(string? MethodSymbolsPath = null, string? SourceDirectory = null);
