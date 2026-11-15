// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class RectangularArrayLayoutProvider(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects) : IRectangularArrayLayoutProvider
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IRuntimeObjectLayout _objects = objects ??
        throw new ArgumentNullException(nameof(objects));

    public RectangularArrayLayout Provide() => RectangularArrayLayout.Create(
        _layouts.Target,
        _objects.ArrayElementTypeIdOffset);
}
