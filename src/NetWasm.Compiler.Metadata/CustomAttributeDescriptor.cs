// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record CustomAttributeDescriptor(
    AssemblyIdentity Source,
    int AttributeToken,
    int ConstructorToken,
    CliTypeIdentity AttributeType,
    CliGenericContext GenericContext);
