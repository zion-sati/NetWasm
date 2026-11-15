// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataSymbolFormatterFactory(
    ITypeRepositoryFactory repositories) : ISymbolFormatterFactory
{
    private readonly ITypeRepositoryFactory _repositories =
        repositories ?? throw new ArgumentNullException(nameof(repositories));

    public ISymbolFormatter Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataSymbolFormatter(_repositories.Create(snapshot));
    }
}
