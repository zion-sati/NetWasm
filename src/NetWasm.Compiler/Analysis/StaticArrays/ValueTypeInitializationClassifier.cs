// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.StaticArrays;

internal sealed class ValueTypeInitializationClassifier(ITypeDefinitionResolver types, IMethodRepository methods)
    : IValueTypeInitializationClassifier
{
    private readonly ITypeDefinitionResolver _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public bool ConstructsWithoutInitialization(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsValueType) return false;
        var owner = _types.ResolveTypeIdentity(type);
        return owner.IsBeforeFieldInit || !owner.Methods.Any(key => _methods.GetMethod(key).Name == ".cctor");
    }
}
