// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.TimeZones;

internal interface IIanaVersionReader
{
    string Read(string sourceDirectory);
}
