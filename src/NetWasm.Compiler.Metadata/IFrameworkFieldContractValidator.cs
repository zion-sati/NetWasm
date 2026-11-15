// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record FrameworkFieldBinding(
    FieldDefinitionModel Original,
    FieldDefinitionModel Target,
    string Caller,
    int IlOffset)
{
    public MetadataAssemblySnapshot? OriginalSource { get; init; }
}

internal interface IFrameworkFieldContractValidator
{
    void Validate(FrameworkFieldBinding binding);
}
