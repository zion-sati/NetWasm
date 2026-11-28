// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Every value type returns through a buffer: the convention while the call paths move to results one by one.
internal sealed class NullStructResultClassifier : IStructResultClassifier
{
    public StructResultShape? Classify(CliTypeIdentity type) => null;
}
