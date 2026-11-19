// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Chooses how a marshalling stub passes each of its parameters and its result, as the streams of CIL each takes.
internal interface INativeMarshallingClassifier
{
    NativeStubStreams Classify(MethodInstanceModel stub, NativeImportDeclaration import, MethodSignatureModel native);
}
