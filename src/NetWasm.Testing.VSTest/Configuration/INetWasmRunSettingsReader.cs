// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Testing.VSTest.Configuration;

internal interface INetWasmRunSettingsReader
{
    bool TryRead(string? runsettingsXml, out NetWasmRunConfiguration? configuration);
}
