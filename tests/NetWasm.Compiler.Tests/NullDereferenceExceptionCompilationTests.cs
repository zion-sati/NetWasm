// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Runtime.Loader;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Each instruction that dereferences an object reference or a managed address throws NullReferenceException through
// a null one, as on desktop, even where it is the program's only way to raise the exception: the program keeps the
// exception it throws (ldind, stind, ldobj, stobj, cpobj, initobj and ldflda), not only the ones a field access or a
// call brings.
public sealed class NullDereferenceExceptionCompilationTests
{
    public static TheoryData<string, WasmTarget, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<string, WasmTarget, FinalWasmOptimization>();
        foreach (var operation in NullDereferenceAssemblyBuilder.Operations)
        {
            cases.Add(operation, WasmTarget.Wasm32, FinalWasmOptimization.None);
            cases.Add(operation, WasmTarget.Wasm32, FinalWasmOptimization.O3);
            cases.Add(operation, WasmTarget.Wasm64, FinalWasmOptimization.O3);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ANullDereferenceThrowsWhereNothingElseRaisesTheException(
        string operation,
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        var bytes = NullDereferenceAssemblyBuilder.Build(operation);
        var desktop = new AssemblyLoadContext("NullDereference.Desktop." + operation, isCollectible: true);
        int[] expected;
        try
        {
            var run = desktop.LoadFromStream(new MemoryStream(bytes)).GetType("EntryPoint")!.GetMethod("Run")!;
            expected = [(int)run.Invoke(null, [0])!, (int)run.Invoke(null, [1])!];
        }
        finally
        {
            desktop.Unload();
        }
        Assert.Equal(new[] { -1, 1 }, expected);

        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "NullDereference_" + operation + ".dll");
        File.WriteAllBytes(assembly, bytes);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory, Optimization: optimization));
        int[] actual =
        [
            CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd),
            CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, 1, target, compilation.StaticDataEnd),
        ];
        Assert.Equal(expected, actual);
    }
}
