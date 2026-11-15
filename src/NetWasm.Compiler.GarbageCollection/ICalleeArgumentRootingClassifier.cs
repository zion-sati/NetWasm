// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.GarbageCollection;

public interface ICalleeArgumentRootingClassifier
{
    // The number of entries on top of the evaluation stack that a call hands
    // to a managed callee rooting them itself, or zero while the call site
    // must keep its arguments rooted.
    int Classify(RootDecisionRequest request);
}
