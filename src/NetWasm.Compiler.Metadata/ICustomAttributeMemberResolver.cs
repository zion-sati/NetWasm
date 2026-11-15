// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record CustomAttributeMemberBinding(FieldInstanceModel? Field, MethodInstanceModel? Setter);

public interface ICustomAttributeMemberResolver
{
    CustomAttributeMemberBinding? Resolve(CliTypeIdentity declaringType, string name,
        CustomAttributeNamedArgumentKind kind);
}
