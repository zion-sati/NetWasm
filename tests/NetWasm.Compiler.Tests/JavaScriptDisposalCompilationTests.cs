// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class JavaScriptDisposalCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DirectDisposalDoesNotRetainUnusedInterfaceBodies(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DirectHostDisposal", """
            using System;
            using System.Runtime.InteropServices.JavaScript;
            public static class DirectHostDisposal
            {
                [JSImport("create", "consumer.disposal")]
                private static extern JSObject Create();
                [JSImport("subscribe", "consumer.disposal")]
                private static extern JSSubscription Subscribe(Action callback);
                private static void Callback() { }
                public static int Run(int input)
                {
                    Create().Dispose();
                    Subscribe(Callback).Dispose();
                    return input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib],
            "DirectHostDisposal", "Run", [], Target: target));
        Assert.Empty(result.Program.DispatchCallSites);
        Assert.DoesNotContain(result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values),
            body => body.Method.Definition.Name == "System.IDisposable.Dispose");
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InterfaceUsingAndGenericDisposalCompile(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(assets.Root, "tests", "end-to-end",
            "host-interop", "javascript", "DisposableInterop.cs"));
        var assembly = assets.CompileSource("DisposableInterop", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib],
            "DisposableInterop", "Run", [], Target: target));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(2, result.InteropManifest.Imports.Length);
        Assert.Single(result.InteropManifest.Callbacks);
        var targets = result.Program.DispatchCallSites.Values
            .Where(site => site.Declaration.DeclaringType.FullName == "System.IDisposable")
            .SelectMany(site => site.Targets).ToArray();
        foreach (var name in new[] { "JSObject", "JSSubscription" })
        {
            var dispatch = Assert.Single(targets
                .Where(candidate => candidate.ReceiverType.FullName ==
                    "System.Runtime.InteropServices.JavaScript." + name)
                .DistinctBy(candidate => candidate.Method.CanonicalName));
            Assert.Equal("System.IDisposable.Dispose", dispatch.Method.Definition.Name);
            Assert.Contains(result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values),
                body => body.Method.CanonicalName == dispatch.Method.CanonicalName);
        }
    }
}
