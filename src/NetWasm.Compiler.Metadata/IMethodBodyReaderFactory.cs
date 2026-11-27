// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodBodyReaderFactory
{
    // wholeProgramSuspension: whether the runtime async lowering may lower a direct await of a method that cannot suspend
    // as a plain call (RuntimeAsyncSuspensionFacts), which compiles each method from its callees' bodies too.
    IMethodBodyReader Create(MetadataCompilationSnapshot snapshot, bool wholeProgramSuspension = false);
}
