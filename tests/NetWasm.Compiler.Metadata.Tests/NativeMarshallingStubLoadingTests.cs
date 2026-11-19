// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// A bound P/Invoke whose signature takes runtime marshalling (a class, an array or a HandleRef, by value or by
// reference) loads as a marshalling stub over an inner native method: the declaration's own token keeps the managed
// signature and no import, and the inner one, under the token with its high bit set, holds the import with each such
// position lowered to a native-sized integer. Everything else stays an import as declared.
public sealed class NativeMarshallingStubLoadingTests
{
    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        public sealed class Handle : SafeHandle
        {
            private Handle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => true;
        }

        public static class Native
        {
            [DllImport("mule", EntryPoint = "e")]
            public static extern int Scalars(int value, IntPtr pointer, ref int count);

            [DllImport("mule", EntryPoint = "e")]
            public static extern Handle Handles(Handle input, ref Handle both, out Handle output, [In] ref Handle input2,
                HandleRef reference, byte[] bytes, [In, Out] int[] values, long scalar);

            [DllImport("mule", EntryPoint = "e", PreserveSig = false)]
            public static extern void Translated(Handle input);

            [DllImport("mule", EntryPoint = "e")]
            public static extern int Described([MarshalAs(UnmanagedType.LPArray)] byte[] bytes);

            [DllImport("mule", EntryPoint = "e")]
            public static extern bool Converted(bool flag, [MarshalAs(UnmanagedType.U1)] bool small, char ansi,
                [MarshalAs(UnmanagedType.U2)] char wide, ref bool both, out char letter);

            [DllImport("mule", EntryPoint = "e", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool Unicode(char wide, [MarshalAs(UnmanagedType.I1)] char narrow);

            [DllImport("mule", EntryPoint = "e")]
            public static extern int Restated([MarshalAs(UnmanagedType.I4)] int value, [MarshalAs(UnmanagedType.U8)] long wide);

            [DllImport("mule", EntryPoint = "e")]
            public static extern int Mismatched([MarshalAs(UnmanagedType.I2)] int value);
        }
        """;

    [Fact]
    public void AMarshalledSignatureLoadsAsAStubOverItsNativeMethod()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("NativeMarshallingStubs", Source));
        var stub = assembly.Methods.Values.Single(method => method.Name == "Handles" && method.NativeMarshallingStub is not null);
        var native = assembly.Methods[stub.NativeMarshallingStub!.NativeMethod.MetadataToken];

        Assert.Null(stub.NativeImport);
        Assert.True(stub.HasManagedBody);
        Assert.False(stub.HasBody);
        Assert.Equal(stub.Key.MetadataToken | unchecked((int)0x80000000), native.Key.MetadataToken);
        Assert.Equal(stub.DeclaringType, native.DeclaringType);
        Assert.Equal("e", native.NativeImport!.EntryPoint);
        Assert.Null(native.NativeMarshallingStub);
        Assert.True(native.IsStatic);
        Assert.DoesNotContain(native.Key, assembly.Types.Values.SelectMany(type => type.Methods));

        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var byReference = CliTypeIdentity.ManagedByReference(nativeInt);
        Assert.Equal(nativeInt, native.Signature.ReturnSignatureType);
        Assert.Equal([nativeInt, byReference, byReference, byReference, nativeInt, nativeInt, nativeInt,
            CliTypeIdentity.Primitive("i8", CliValueKind.I8)], native.Signature.ParameterSignatureTypes);
        Assert.Equal([NativeParameterFlow.None, NativeParameterFlow.None, NativeParameterFlow.Out, NativeParameterFlow.In,
            NativeParameterFlow.None, NativeParameterFlow.None, NativeParameterFlow.In | NativeParameterFlow.Out,
            NativeParameterFlow.None], native.NativeImport.ParameterFlows.Values);
    }

    // bool and char positions lower to the native scalar desktop .NET's runtime marshalling passes on Unix: a BOOL, a
    // one-byte bool, an ANSI byte or a UTF-16 code unit, by value or by reference, as the import's [MarshalAs] and
    // CharSet choose; each position's [MarshalAs] is recorded for the stub.
    [Fact]
    public void BoolAndCharPositionsLowerToTheNativeScalarsTheyMarshalAs()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("NativeMarshallingStubs", Source));
        MethodDefinitionModel Native(string name) => assembly.Methods[assembly.Methods.Values
            .Single(method => method.Name == name && method.NativeMarshallingStub is not null).NativeMarshallingStub!
            .NativeMethod.MetadataToken];
        CliTypeIdentity Scalar(string name) => CliTypeIdentity.Primitive(name, CliValueKind.I4);

        var converted = Native("Converted");
        Assert.Equal(Scalar("i4"), converted.Signature.ReturnSignatureType);
        Assert.Equal([Scalar("i4"), Scalar("u1"), Scalar("u1"), Scalar("u2"), CliTypeIdentity.ManagedByReference(Scalar("i4")),
            CliTypeIdentity.ManagedByReference(Scalar("u1"))], converted.Signature.ParameterSignatureTypes);
        Assert.Equal([null, System.Runtime.InteropServices.UnmanagedType.U1, null, System.Runtime.InteropServices.UnmanagedType.U2,
            null, null], converted.NativeImport!.MarshalAs.Parameters);
        Assert.False(converted.NativeImport.HasMarshalling);

        var unicode = Native("Unicode");
        Assert.Equal(Scalar("i1"), unicode.Signature.ReturnSignatureType);
        Assert.Equal([Scalar("u2"), Scalar("i1")], unicode.Signature.ParameterSignatureTypes);
        Assert.Equal(System.Runtime.InteropServices.UnmanagedType.I1, unicode.NativeImport!.MarshalAs.Result);

        // A [MarshalAs] its position cannot take makes a stub whose builder reports it where reached.
        Assert.Equal([Scalar("i4")], Native("Mismatched").Signature.ParameterSignatureTypes);
    }

    [Theory]
    [InlineData("Scalars")]
    [InlineData("Translated")]
    [InlineData("Described")]
    [InlineData("Restated")]
    public void OtherSignaturesStayImports(string name)
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("NativeMarshallingStubs", Source));
        var method = assembly.Methods.Values.Single(method => method.Name == name);

        Assert.NotNull(method.NativeImport);
        Assert.Null(method.NativeMarshallingStub);
    }

    // Desktop .NET rejects handles and arrays under disabled runtime marshalling, so they stay imports, which the
    // native ABI rejects where reached.
    [Fact]
    public void DisabledRuntimeMarshallingLeavesEveryImportAsDeclared()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("NativeMarshallingDisabled",
            Source.Replace("public sealed class Handle", "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n" +
                "public sealed class Handle", StringComparison.Ordinal)));

        Assert.All(assembly.Methods.Values.Where(method => method.NativeImport is not null || method.NativeMarshallingStub is not null),
            method => Assert.Null(method.NativeMarshallingStub));
        Assert.Equal(8, assembly.Methods.Values.Count(method => method.NativeImport is not null));
    }
}
