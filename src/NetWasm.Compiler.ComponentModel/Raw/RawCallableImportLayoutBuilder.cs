// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed class RawCallableImportLayoutBuilder(IRawWitFunctionLayoutBuilder functions) : IRawWitImportLayoutBuilder
{
    private readonly IRawWitFunctionLayoutBuilder _functions = functions ?? throw new ArgumentNullException(nameof(functions));

    public RawWitImportLayout Build(RawWitImportLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Declaration is not RawWitImportDeclaration.Callable callable)
        {
            throw ComponentException.Invalid("callable layout builder requires a callable declaration");
        }
        return new RawWitImportLayout.Callable(_functions.Build(
            request.Document,
            callable.InterfaceName,
            callable.CoreInterfaceName,
            callable.Definition,
            request.Target,
            callable.Asynchronous));
    }
}
