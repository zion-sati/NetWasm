// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataMethodBodyBlockReader
{
    MethodBodyBlock Read(PEReader source, MethodDefinitionModel method);
}
