// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Aliases;

public interface IWitAliasValidator
{
    void Validate(WitDocument document, WitTypeDefinition definition);
}
