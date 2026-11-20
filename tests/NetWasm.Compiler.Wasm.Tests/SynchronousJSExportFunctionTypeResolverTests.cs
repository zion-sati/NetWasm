// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class SynchronousJSExportFunctionTypeResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("in-memory");

    [Fact]
    public void ResolvesManagedBuffersValuesAndScalarsToTheirBoundaryTypes()
    {
        var stringType = CliTypeIdentity.Named(
            Assembly, "System", "String", isValueType: false);
        var byteArray = CliTypeIdentity.SzArray(CliTypeIdentity.Named(
            Assembly, "System", "Byte", isValueType: true));
        var valueType = CliTypeIdentity.Named(
            Assembly, "Example", "Value", isValueType: true);
        var scalar = CliTypeIdentity.Named(
            Assembly, "System", "Int64", isValueType: true);
        var method = new MethodDefinitionModel(
            new(Assembly, 1),
            new(Assembly, 2),
            "Export",
            true,
            MethodSignatureModel.Create(scalar, stringType, byteArray, valueType, scalar),
            1);
        var resolver = Assert.IsAssignableFrom<ISynchronousJSExportFunctionTypeResolver>(
            new SynchronousJSExportFunctionTypeResolver());

        var result = resolver.Resolve(method);

        // A string crosses as a handle; a byte array as its staged block's address and its length.
        Assert.Equal(
            [CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.I8],
            result.Parameters.ToArray());
        Assert.Equal(CliValueKind.I8, result.Result);
    }

    [Fact]
    public void RejectsAMissingMethod() =>
        Assert.Throws<ArgumentNullException>(() =>
            new SynchronousJSExportFunctionTypeResolver().Resolve(null!));
}
