// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeUndefinedSymbolReporter
{
    // The functions a failed link left undefined, as an error that names them and how to supply them; null when the
    // link failed for another reason.
    InvalidOperationException? Explain(string linkLog, bool rawWasmOutput, Exception failure);
}
