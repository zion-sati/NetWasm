// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed class RawResourceImportLayoutBuilder(IRawResourceIntrinsicLayoutPlanner resources) : IRawWitImportLayoutBuilder
{
    private readonly IRawResourceIntrinsicLayoutPlanner _resources = resources ?? throw new ArgumentNullException(nameof(resources));

    public RawWitImportLayout Build(RawWitImportLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Declaration is not RawWitImportDeclaration.Resource resource)
        {
            throw ComponentException.Invalid("resource layout builder requires a resource declaration");
        }
        return new RawWitImportLayout.Resource(_resources.Plan(request.Document, resource, request.Target));
    }
}
