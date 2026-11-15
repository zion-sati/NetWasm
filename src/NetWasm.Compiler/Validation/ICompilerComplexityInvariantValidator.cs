// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Validation;

public interface ICompilerComplexityInvariantValidator
{
    void Validate(CompilerComplexityReport report);
}
