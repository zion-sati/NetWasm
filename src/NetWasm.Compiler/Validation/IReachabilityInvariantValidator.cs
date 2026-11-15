// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Validation;

internal interface IReachabilityInvariantValidator
{
    void Validate(
        IMethodInstanceResolver methodInstances,
        ITypeClassifier types,
        ReachableProgram program,
        IRuntimeIntrinsicRegistry intrinsics);
}
