// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A call with the tail. prefix (compiled XAML emits it) runs as an ordinary call and returns what desktop .NET returns,
// tail recursion included.
public sealed class TailCallPrefixCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void TailPrefixedCallsReturnTheCalleesResult(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var image = Build();
        var assembly = Path.Combine(assets.Directory, $"TailCalls{target}{optimization}.dll");
        File.WriteAllBytes(assembly, image);
        var desktop = Assembly.Load(image).GetType("TailCalls.EntryPoint")!.GetMethod("Run")!;

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TailCalls.EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: CoreLibAlias, ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);

        Assert.Equal(42, (int)desktop.Invoke(null, [41])!);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 41, target, compilation.StaticDataEnd));
    }

    private static readonly ImmutableDictionary<string, string> CoreLibAlias =
        ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib");

    // Run(n) is tail. call Count(n, 1); Count(n, total) is total when n is 0, and tail. call Count(n - 1, total + 1)
    // otherwise.
    private static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("TailCalls"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("TailCalls.dll");
        var entry = module.DefineType("TailCalls.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var count = entry.DefineMethod("Count", MethodAttributes.Public | MethodAttributes.Static, typeof(int),
            [typeof(int), typeof(int)]);
        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);

        var code = count.GetILGenerator();
        var recurse = code.DefineLabel();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Brtrue_S, recurse);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(recurse);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Sub);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Tailcall);
        code.Emit(OpCodes.Call, count);
        code.Emit(OpCodes.Ret);

        code = run.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Tailcall);
        code.Emit(OpCodes.Call, count);
        code.Emit(OpCodes.Ret);

        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
