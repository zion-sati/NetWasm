// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InteropTypeClassifierTests
{
    [Fact]
    public void ClassifiesOnlySupportedInteropShapes()
    {
        var stringType = CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false);
        var bytes = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var jsObject = CliTypeIdentity.Named(
            Assembly,
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            isValueType: false);
        var subscription = CliTypeIdentity.Named(
            Assembly,
            "System.Runtime.InteropServices.JavaScript",
            "JSSubscription",
            isValueType: false);

        Assert.True(InteropTypeClassifier.IsString(stringType));
        Assert.True(InteropTypeClassifier.IsValueArray(bytes));
        Assert.True(InteropTypeClassifier.IsJSObject(jsObject));
        Assert.True(InteropTypeClassifier.IsSubscription(subscription));
        Assert.True(InteropTypeClassifier.IsHostObject(jsObject));
        Assert.True(InteropTypeClassifier.IsHostObject(subscription));
        Assert.False(InteropTypeClassifier.IsHostObject(stringType));
        Assert.True(InteropTypeClassifier.IsValueArray(CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4))));
        Assert.False(InteropTypeClassifier.IsValueArray(CliTypeIdentity.SzArray(stringType)));
    }
}
