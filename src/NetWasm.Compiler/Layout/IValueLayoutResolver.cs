// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IValueLayoutResolver
{
    ValueLayout Resolve(CliTypeIdentity type);

    ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition);
}
