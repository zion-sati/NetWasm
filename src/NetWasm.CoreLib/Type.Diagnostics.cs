// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System;

public partial class Type
{
    // NetWasm: a type's names come from the type-symbols sidecar when the program loaded one, and otherwise read as
    // the type's ID, the same text however the Type was reached (issue 225). The namespace has no such stand-in: it
    // is null, as for a type without one.
    public override string Name => TypeSymbols.Find(SemanticTypeId)?.Name ?? TypeSymbols.IdName(SemanticTypeId);

    public string? Namespace => this is SharedArrayType shared
        ? shared.SharedNamespace
        : TypeSymbols.Find(SemanticTypeId)?.Namespace;

    public string? FullName => this is SharedArrayType shared
        ? shared.SharedFullName
        : TypeSymbols.Find(SemanticTypeId) is { } symbol
            ? symbol.FullName
            : TypeSymbols.IdName(SemanticTypeId);

    public override string ToString() =>
        TypeSymbols.Find(SemanticTypeId)?.DisplayName ?? TypeSymbols.IdName(SemanticTypeId);
}
