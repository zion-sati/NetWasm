// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// A [JSImport] whose body .NET's browser binding generator wrote ([DebuggerNonUserCode]) is NetWasm's own import, its
// body set aside, where a JavaScript host runs the program and NetWasm's interop takes its shape; one whose shape it
// does not take stays ordinary code, declined with the reason. Without a JavaScript host every such import stays
// ordinary code, as on .NET's platforms without a browser.
public sealed class GeneratedJavaScriptBindingLoadingTests
{
    private const string Source = """
        using System.Diagnostics;
        using System.Runtime.InteropServices.JavaScript;

        public static partial class Imports
        {
            [JSImport("add", "lib.math")]
            [DebuggerNonUserCode]
            internal static int Add(int a, int b) => a + b;

            [JSImport("mismatched", "lib.math")]
            [DebuggerNonUserCode]
            internal static int Mismatched([JSMarshalAs<JSType.String>] int value) => value;
        }
        """;

    [Fact]
    public void AGeneratedImportNetWasmCannotTakeIsDeclinedWhereAJavaScriptHostRunsTheProgram()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("GeneratedBindings", Source),
            javaScriptBinding: GeneratedJavaScriptBinding.HostInterop);

        var add = assembly.Methods.Values.Single(method => method.Name == "Add");
        var mismatched = assembly.Methods.Values.Single(method => method.Name == "Mismatched");

        Assert.Equal((true, 0, null), (add.JSImport is not null, add.RelativeVirtualAddress, add.DeclinedJavaScriptBinding));
        Assert.Null(mismatched.JSImport);
        Assert.NotEqual(0, mismatched.RelativeVirtualAddress);
        Assert.Equal("JSImport", mismatched.DeclinedJavaScriptBinding!.Attribute);
        Assert.Contains("JSMarshalAs<", mismatched.DeclinedJavaScriptBinding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAJavaScriptHostGeneratedImportsStayOrdinaryCode()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CompileUnsafeSource("OrdinaryBindings", Source),
            javaScriptBinding: GeneratedJavaScriptBinding.OrdinaryCode);

        Assert.All(assembly.Methods.Values.Where(method => method.Name is "Add" or "Mismatched"), method =>
            Assert.Equal((null, true, null), (method.JSImport, method.RelativeVirtualAddress != 0, method.DeclinedJavaScriptBinding)));
    }
}
