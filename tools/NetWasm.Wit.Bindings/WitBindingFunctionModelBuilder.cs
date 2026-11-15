// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings;

public sealed record WitBindingFunctionModel(
    CanonicalAbiFunction Function,
    CanonicalAbiCoreSignature Signature);

public interface IWitBindingFunctionModelBuilder
{
    WitBindingFunctionModel Build(
        WitDocument document,
        string interfaceName,
        WitFunction witFunction,
        CanonicalAbiDirection direction);
}

public sealed class WitBindingFunctionModelBuilder(
    IWitCanonicalFunctionBuilder functions,
    ICanonicalAbiSignaturePlanner signatures) : IWitBindingFunctionModelBuilder
{
    private readonly IWitCanonicalFunctionBuilder _functions = functions ??
        throw new ArgumentNullException(nameof(functions));
    private readonly ICanonicalAbiSignaturePlanner _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));

    public WitBindingFunctionModel Build(
        WitDocument document,
        string interfaceName,
        WitFunction witFunction,
        CanonicalAbiDirection direction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(interfaceName);
        ArgumentNullException.ThrowIfNull(witFunction);
        var canonical = _functions.Build(document, interfaceName, witFunction);
        return new(canonical, _signatures.Plan(canonical, direction));
    }
}
