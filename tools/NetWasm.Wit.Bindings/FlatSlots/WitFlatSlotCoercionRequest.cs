// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.FlatSlots;

public sealed record WitFlatSlotCoercionRequest(
    string Expression,
    CliValueKind Source,
    CliValueKind Destination);
