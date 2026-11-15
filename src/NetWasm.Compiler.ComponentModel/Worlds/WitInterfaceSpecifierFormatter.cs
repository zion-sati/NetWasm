// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Worlds;

public sealed class WitInterfaceSpecifierFormatter : IWitInterfaceSpecifierFormatter
{
    public string Format(WitInterface definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Key;
    }
}
