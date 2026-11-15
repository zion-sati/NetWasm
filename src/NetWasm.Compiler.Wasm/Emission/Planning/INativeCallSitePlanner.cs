// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface INativeCallSitePlanner
{
    // Lowers each unmanaged calli signature as a DllImport's and gives each physical signature a
    // trampoline import, numbered from firstImportIndex.
    NativeCallSitePlan Plan(IEnumerable<NativeCallSiteUse> uses, int firstImportIndex);
}
