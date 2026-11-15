// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface IRootMapInvariantValidator
{
    void Validate(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ReachableProgram program,
        ITypeClassifier typeClassifier,
        IReadOnlySet<string>? restored = null);
}
