// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal sealed record RuntimeProvidedMethodRequest(
    AssemblyIdentity SourceAssembly,
    int MethodToken,
    int DeclaringTypeToken,
    CliTypeIdentity DeclaringType,
    string Name,
    bool IsInstance,
    MethodSignatureModel Signature,
    string MethodDisplayName,
    int IlOffset);
