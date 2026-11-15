// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkedRuntimeModuleReader
{
    RuntimeLinkedModule Read(ReadOnlyMemory<byte> module);
}
