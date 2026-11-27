// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Debugging;

public interface IWasmDebugInformationWriter
{
    // Writes debug information into the linked core module at modulePath: from the stack-trace markers its code holds
    // and the program's method symbols, function names and DWARF lines; without method symbols the module is left as
    // it is.
    void Write(string modulePath, WasmDebugInformation debugInformation);
}
