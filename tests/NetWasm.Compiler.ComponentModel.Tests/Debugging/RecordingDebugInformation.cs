// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

// Records each module the packaging asks for debug lines, without touching it.
internal sealed class RecordingDebugInformation : IWasmDebugInformationWriter
{
    public List<(string ModulePath, WasmDebugInformation DebugInformation)> Calls { get; } = [];

    public void Write(string modulePath, WasmDebugInformation debugInformation) =>
        Calls.Add((modulePath, debugInformation));
}
