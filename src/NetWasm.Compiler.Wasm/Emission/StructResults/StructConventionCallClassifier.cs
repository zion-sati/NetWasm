// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Whether a call runs the called method's own function, which takes a struct argument by its fields and returns a
// struct in results, so its arguments and result may stay in lanes or in the caller's parameters. A method the
// compiler expands in place (a metadata intrinsic, or one the runtime intrinsic registry names, such as the tuple
// Math.DivRem) and a method with no managed body (an import) move their values their own way.
internal interface IStructConventionCallClassifier
{
    bool CallsBody(MethodInstanceModel called);
}

internal sealed class StructConventionCallClassifier(IRuntimeIntrinsicRegistry intrinsics) : IStructConventionCallClassifier
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public bool CallsBody(MethodInstanceModel called)
    {
        ArgumentNullException.ThrowIfNull(called);
        var definition = called.Definition;
        return !definition.IsIntrinsic && (definition.HasManagedBody || definition.RuntimeAsyncBodyOf is not null) &&
            !_intrinsics.TryGetIntrinsic(definition.Key, out _);
    }
}
