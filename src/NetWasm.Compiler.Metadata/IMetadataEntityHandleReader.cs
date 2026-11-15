// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataEntityHandleReader
{
    EntityHandle Read(
        int token,
        string kind,
        string method,
        int ilOffset);
}
