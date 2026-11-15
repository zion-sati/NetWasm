// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record FilterCapture(
    CapturedSlot Slot,
    CliTypeIdentity Type,
    int Offset,
    ImmutableArray<(int ByteOffset, int RootSlot)> Roots);
