// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// A C variadic function declared through fixed-signature overloads loads as one variadic stub per overload, when the
// linking planner finds it variadic: each overload's inner native method takes the fixed parameters and the address of
// the variadic arguments' buffer, so they all share one signature. Declarations of one entry point the planner does
// not find variadic, and a single declaration, load as before.
public sealed class NativeVariadicStubLoadingTests
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

        public static unsafe class Native
        {
            [DllImport("mule", EntryPoint = "config")]
            public static extern int None(int op);

            [DllImport("mule", EntryPoint = "config")]
            public static extern int Pointers(int op, IntPtr pointer, int* result);

            [DllImport("mule", EntryPoint = "config")]
            public static extern int Handles(int op, Handle handle);

            [DllImport("mule", EntryPoint = "single")]
            public static extern int Single(int op, int value);

            [DllImport("other", EntryPoint = "config")]
            public static extern int Other(int op, long value);
        }
        """;

    [Fact]
    public void OverloadsOfAVariadicFunctionShareTheFixedParametersAndABuffer()
    {
        using var assets = TestAssets.Create();
        var planner = new FixedPlanner(1);
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("VariadicStubs", Source),
            NativeLibraryBinding.All, planner);
        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

        foreach (var name in new[] { "None", "Pointers", "Handles" })
        {
            var stub = assembly.Methods.Values.Single(method => method.Name == name && method.NativeMarshallingStub is not null);
            var native = assembly.Methods[stub.NativeMarshallingStub!.NativeMethod.MetadataToken];
            Assert.Equal(new NativeVariadicCall(1), stub.NativeMarshallingStub.Variadic);
            Assert.Equal([int32, nativeInt], native.Signature.ParameterSignatureTypes);
            Assert.Equal(int32, native.Signature.ReturnSignatureType);
            Assert.Equal("config", native.NativeImport!.EntryPoint);
        }
        // One question per library's entry point declared more than once: mule's config, with each declaration's
        // native signature (the handle lowered to a native integer).
        var (library, entry, declarations) = Assert.Single(planner.Questions);
        Assert.Equal(("mule", "config"), (library, entry));
        Assert.Equal(3, declarations.Count);
        Assert.Contains(declarations, signature => signature.ParameterSignatureTypes is [_, var handle] && handle == nativeInt);
        Assert.Null(assembly.Methods.Values.Single(method => method.Name == "Single").NativeMarshallingStub);
        Assert.Null(assembly.Methods.Values.Single(method => method.Name == "Other").NativeMarshallingStub);
    }

    [Fact]
    public void OverloadsThePlannerDoesNotFindVariadicLoadAsBefore()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("NotVariadicStubs", Source),
            NativeLibraryBinding.All, new FixedPlanner(null));

        Assert.Null(assembly.Methods.Values.Single(method => method.Name == "None").NativeMarshallingStub);
        Assert.Null(assembly.Methods.Values.Single(method => method.Name == "Pointers").NativeMarshallingStub);
        var handles = assembly.Methods.Values.Single(method => method.Name == "Handles" && method.NativeMarshallingStub is not null);
        Assert.Null(handles.NativeMarshallingStub!.Variadic);
    }

    // Under disabled runtime marshalling a variadic call passes only blittable values: overloads of those still share
    // the fixed parameters and a buffer, while one with a handle stays an import as declared, which the native ABI
    // rejects where reached.
    [Fact]
    public void DisabledRuntimeMarshallingKeepsOnlyBlittableVariadicCalls()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("DisabledVariadicStubs",
            Source.Replace("public sealed class Handle", "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n" +
                "public sealed class Handle", StringComparison.Ordinal)), NativeLibraryBinding.All,
            new FixedPlanner(1));

        foreach (var name in new[] { "None", "Pointers" })
        {
            var stub = assembly.Methods.Values.Single(method => method.Name == name && method.NativeMarshallingStub is not null);
            Assert.Equal(new NativeVariadicCall(1), stub.NativeMarshallingStub!.Variadic);
        }
        var handles = assembly.Methods.Values.Single(method => method.Name == "Handles");
        Assert.Null(handles.NativeMarshallingStub);
        Assert.NotNull(handles.NativeImport);
    }

    // Unbound overloads (a library the program does not link) are no variadic call either.
    [Fact]
    public void UnboundOverloadsAreNotAsked()
    {
        using var assets = TestAssets.Create();
        var planner = new FixedPlanner(1);
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("UnboundVariadicStubs", Source),
            new NativeLibraryBinding(["other"]), planner);

        Assert.Empty(planner.Questions);
        Assert.Null(assembly.Methods.Values.Single(method => method.Name == "Pointers").NativeMarshallingStub);
    }

    private sealed class FixedPlanner(int? fixedParameters) : INativeVariadicCallPlanner
    {
        public List<(string Library, string Entry, IReadOnlyCollection<MethodSignatureModel> Declarations)> Questions { get; } = [];

        public int? FixedParameters(string libraryName, string entryPoint, IReadOnlyCollection<MethodSignatureModel> declarations)
        {
            Questions.Add((libraryName, entryPoint, declarations));
            return fixedParameters;
        }
    }
}
