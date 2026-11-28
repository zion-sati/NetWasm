// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A member descriptor is static data the compiler writes: a property's getter and an accessor's property are fields no
// IL stores, so the receiver flow takes them as the runtime's. Expression trees reach a property through its getter's
// descriptor and then call the getter's virtual members (IsStatic, GetParameters), which dispatch on what those fields
// hold: an instance property and a field.
public sealed class ReceiverFlowMemberDescriptorCompilationTests
{
    internal const string Source = """
        using System;
        using System.Linq.Expressions;

        namespace ReceiverFlowMemberDescriptorFixture;

        public sealed class Model
        {
            public Model(string name, int size)
            {
                Name = name;
                Size = size;
            }

            public string Name { get; }

            public int Size;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Expression<Func<Model, string>> name = model => model.Name;
                Expression<Func<Model, int>> size = model => model.Size;
                var model = new Model("Ada", input);
                var total = name.Compile()(model) == "Ada" ? 1 : 0;
                total += size.Compile()(model) == input ? 10 : 0;
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AccessorsTheCompilerWritesReachTheirCalls(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ReceiverFlowMemberDescriptorFixture", Source, TestAssets.RepositorySdkVersion, "latest", optimize: true);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ReceiverFlowMemberDescriptorFixture.EntryPoint", "Run", [],
            Optimization: optimization));

        Assert.Equal(11, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }
}
