// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface INativeCallbackDeclarationReader
{
    NativeCallbackDeclaration? Read(MetadataReader metadata, MethodDefinition method);
}
