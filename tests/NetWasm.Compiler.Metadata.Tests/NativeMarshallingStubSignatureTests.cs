// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class NativeMarshallingStubSignatureTests
{
    private static readonly AssemblyIdentity CoreLib = new("System.Private.CoreLib");

    // A custom modifier (modreq or modopt) leaves the position's marshalling as its type's: a modified class converts
    // to a native-sized integer, a modified int32 passes unchanged.
    [Fact]
    public void AModifiedPositionMarshalsAsItsType()
    {
        var modifier = CliTypeIdentity.Named(CoreLib, "System.Runtime.CompilerServices", "IsVolatile", false);
        var text = CliTypeIdentity.Named(CoreLib, "System", "String", false);
        var int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);

        var lowered = NativeMarshallingStubSignature.Lower(new MethodSignatureModel(int32,
            [CliTypeIdentity.Modified(text, modifier, isRequired: true), CliTypeIdentity.Modified(int32, modifier, isRequired: false)]));

        Assert.NotNull(lowered);
        Assert.Equal(CliValueKind.NativeInt, lowered.ParameterSignatureTypes[0].StackKind);
        Assert.Equal(CliTypeShape.Modified, lowered.ParameterSignatureTypes[1].Shape);
    }

    // Where the import keeps runtime marshalling, a modified bool or char is the scalar its type marshals as, by value
    // and by reference.
    [Fact]
    public void AModifiedBoolMarshalsAsItsNativeScalar()
    {
        var modifier = CliTypeIdentity.Named(CoreLib, "System.Runtime.CompilerServices", "IsConst", false);
        var flag = CliTypeIdentity.Modified(CliTypeIdentity.Primitive("bool", CliValueKind.I4), modifier, isRequired: false);
        var import = new NativeImportDeclaration("mule", "f", default, IsVarArg: false, HasMarshalling: false, null);

        var lowered = NativeMarshallingStubSignature.Lower(new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            [flag, CliTypeIdentity.ManagedByReference(flag)]), import)!;

        Assert.Equal(["primitive:i4", "primitive:i4&"], lowered.ParameterSignatureTypes.Select(type => type.CanonicalName));
    }
}
