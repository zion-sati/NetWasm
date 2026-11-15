// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorSignatureReader
{
    MethodSignature<CliTypeIdentity> Read(MethodDefinitionModel method);
    CliTypeIdentity Read(FieldDefinitionModel field);
}
