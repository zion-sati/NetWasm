// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Debugging;

// A linked module's debug information: the documents (a row's file is its 1-based index here), the size of the code
// section's contents, the functions with source lines, the managed name of each function with markers, and the
// function names the module's name section already holds. Addresses count from the start of the code section's
// contents, as Wasm DWARF counts them.
internal sealed record WasmDebugLines(
    ImmutableArray<string> Files,
    int CodeSize,
    ImmutableArray<WasmDebugFunction> Functions,
    ImmutableArray<(uint Function, string Name)> Names,
    ImmutableArray<(uint Function, string Name)> ExistingNames);

// A function's managed name, its body's address range and its line rows in address order.
internal sealed record WasmDebugFunction(string Name, int Low, int High, ImmutableArray<WasmDebugRow> Rows);

// From this address on, the code belongs to this line of this file; line 0 is code with no source line.
internal readonly record struct WasmDebugRow(int Address, int File, int Line);
