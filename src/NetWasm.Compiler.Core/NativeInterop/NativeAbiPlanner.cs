// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed class NativeAbiPlanner(
    INativeDeclarationValidator declarations,
    INativeAbiSignaturePlanner signatures) : INativeAbiPlanner
{
    private readonly INativeDeclarationValidator _declarations =
        declarations ?? throw new ArgumentNullException(nameof(declarations));
    private readonly INativeAbiSignaturePlanner _signatures =
        signatures ?? throw new ArgumentNullException(nameof(signatures));

    public NativeAbiPlan Plan(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        _declarations.Validate(method);
        return new(method.Definition.NativeImport!, _signatures.Plan(method.Signature,
            NativeAbiSignatureKind.Import, method.CanonicalName));
    }
}
