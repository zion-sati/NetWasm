// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.ControlFlow;

public sealed class TypedStackValidatorFactory(
    IStackTypeCompatibilityValidator stackTypeCompatibility) : ITypedStackValidatorFactory
{
    private readonly IStackTypeCompatibilityValidator _stackTypeCompatibility =
        stackTypeCompatibility ??
        throw new ArgumentNullException(nameof(stackTypeCompatibility));

    public ITypedStackValidator Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods) => new TypedStackValidator(
            types,
            fields,
            methods,
            _stackTypeCompatibility);
}
