// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record FrameworkMethodBinding(
    MethodDefinitionModel Original,
    MethodDefinitionModel Target,
    string Caller,
    int IlOffset)
{
    public MetadataAssemblySnapshot? OriginalSource { get; init; }
}

internal interface IFrameworkMethodContractValidator
{
    void Validate(FrameworkMethodBinding binding);
}
