// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Whether a managed method returning the value type returns its fields as Wasm results rather than through a buffer
// its caller passes. The answer depends on the type alone, never on the method or the optimization level, because every
// method a vtable or interface slot, a shared dispatch body, a delegate's Invoke or a compiled unit reaches must agree
// on one signature.
internal interface IStructResultClassifier
{
    // The fields to return, or null where the value goes through a buffer.
    StructResultShape? Classify(CliTypeIdentity type);
}
