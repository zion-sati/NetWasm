// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeLibraryBindingTests
{
    [Theory]
    [InlineData("libsample", true)]
    [InlineData("libmissing", false)]
    // Every program links the C library (CoreLib's Math and MathF import libm from it), linked libraries or not.
    [InlineData("libc", true)]
    // Library names match exactly, as native materialization selects providers.
    [InlineData("LIBC", false)]
    [InlineData("libc.so.6", false)]
    public void AnImportBindsWhenTheProgramLinksItsLibrary(string library, bool binds)
    {
        var binding = new NativeLibraryBinding(["libsample"]);

        Assert.Equal(binds, binding.Binds(Import(library)));
    }

    [Fact]
    public void TheCLibraryBindsWhenNoLibraryIsLinked() =>
        Assert.True(new NativeLibraryBinding([]).Binds(Import(NativeLibraryBinding.CLibrary)));

    [Fact]
    public void AnImportWithWasmImportLinkageAlwaysBinds() =>
        Assert.True(new NativeLibraryBinding([]).Binds(Import("host") with { HasWasmImportLinkage = true }));

    [Fact]
    public void WithoutALinkedLibraryListEveryImportBinds() =>
        Assert.True(NativeLibraryBinding.All.Binds(Import("libmissing")));

    [Fact]
    public void ANullImportIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => NativeLibraryBinding.All.Binds(null!));

    private static NativeImportDeclaration Import(string library) =>
        new(library, "entry", MethodImportAttributes.CallingConventionCDecl, false, false, null);
}
