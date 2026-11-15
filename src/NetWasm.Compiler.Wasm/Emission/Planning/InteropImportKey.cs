// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// Names the raw interop-ABI import of a JSImport method. Only the JSImport call
// emitters resolve it; every other caller of a JSImport uses the managed-ABI entry.
internal readonly record struct InteropImportKey(EntityKey Method);
