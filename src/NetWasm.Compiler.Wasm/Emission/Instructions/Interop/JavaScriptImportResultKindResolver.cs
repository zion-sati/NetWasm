// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportResultKindResolver
{
    JavaScriptImportResultKind Resolve(CliTypeIdentity type);
}

internal sealed class JavaScriptImportResultKindResolver :
    IJavaScriptImportResultKindResolver
{
    public JavaScriptImportResultKind Resolve(CliTypeIdentity type) => type switch
    {
        _ when InteropTypeClassifier.IsString(type) =>
            JavaScriptImportResultKind.String,
        _ when InteropTypeClassifier.IsValueArray(type) =>
            JavaScriptImportResultKind.ByteArray,
        _ when InteropTypeClassifier.IsHostObject(type) =>
            JavaScriptImportResultKind.HostObject,
        _ when InteropTypeClassifier.IsDate(type) =>
            JavaScriptImportResultKind.Date,
        _ when InteropTypeClassifier.IsNullable(type) =>
            JavaScriptImportResultKind.Nullable,
        _ when InteropTypeClassifier.IsHandleValue(type) =>
            JavaScriptImportResultKind.HandleValue,
        _ => JavaScriptImportResultKind.Scalar,
    };
}
