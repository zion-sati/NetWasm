// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Types;

public interface IStaticFieldInitializationClassifier
{
    // Whether the accessed field's type is already initialized wherever this
    // access in the body runs, so the access needs no initialization check.
    bool IsInitializedFor(
        MethodDefinitionModel accessor,
        CliTypeIdentity? accessorDeclaringType,
        ImmutableArray<CilInstruction> body,
        CilInstruction access);
}
