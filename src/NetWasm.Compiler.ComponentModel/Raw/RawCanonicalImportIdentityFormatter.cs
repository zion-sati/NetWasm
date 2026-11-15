// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawCanonicalImportIdentityFormatter
{
    RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target);
}

public sealed class RawCanonicalImportIdentityFormatter : IRawCanonicalImportIdentityFormatter
{
    public RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(abiFunction);
        ArgumentNullException.ThrowIfNull(abiFunction.InterfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiFunction.FunctionName);
        return new(CanonicalAbiNames.ImportModule(abiFunction, target), CanonicalAbiNames.ImportName(abiFunction));
    }
}
