// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeImports;

// A native import that takes or returns a value type by value has a forwarder beside it (MethodDefinitionModel.
// NativeImportForwarder): static, with the import's signature, calling the import. Its token is the import's with bit 29
// set, which no metadata token has (bits 31 and 30 mark a marshalling stub's inner method, a runtime-async body and a
// resumption stub).
internal static class NativeImportForwarders
{
    internal static int Token(int importToken) => importToken | 0x20000000;

    // Whether an import needs one: a value type by value among its parameters or its result.
    internal static bool Needs(MethodSignatureModel signature) =>
        signature.ReturnSignatureType.StackKind == CliValueKind.ValueType ||
        signature.ParameterSignatureTypes.Any(static type => type.StackKind == CliValueKind.ValueType);
}
