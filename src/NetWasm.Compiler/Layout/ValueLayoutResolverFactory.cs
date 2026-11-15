// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueLayoutResolverFactory(
    IExplicitValueLayoutResolver explicitLayouts) : IValueLayoutResolverFactory
{
    private readonly IExplicitValueLayoutResolver _explicitLayouts =
        explicitLayouts ?? throw new ArgumentNullException(nameof(explicitLayouts));

    public IValueLayoutResolver Create(
        ITypeDefinitionResolver types,
        IFieldRepository fields,
        WasmTargetLayout target,
        ManagedValueLayoutState state) =>
        new ValueLayoutResolver(types, fields, _explicitLayouts, target, state, new FieldLayoutFailureBinder());
}
