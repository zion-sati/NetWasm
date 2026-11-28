// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Avalonia's XAML compiler emits its dynamic setters with values left on the evaluation stack across a type test's
// branch: `ldarg.0; ldarg.1; dup; isinst T; brfalse`, then each arm stores the value and calls a member of the
// receiver still on the stack (ActiproSoftware.Avalonia.Shared's <>XamlDynamicSetter_29). Roslyn leaves a value on the
// stack across a branch only within an expression (StackAcrossConditionalCompilationTests), never across a type test's
// branch: at Oz one pass rewrote this method into a stack underflow (NW1002) where O0 compiled it.
public sealed class StackAcrossTypeTestBranchCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O0)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Os)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void ValuesLeftOnTheStackAcrossATypeTestBranchReachBothArms(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var path = Path.Combine(assets.Directory, "StackAcrossBranch.dll");
        WriteSetterProgram(path);

        var result = NetWasmCompiler.Compile(new(path, [assets.CoreLib], "StackAcrossBranch.EntryPoint",
            "Run", [new("run", "StackAcrossBranch.EntryPoint", "Run")], Target: target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            Optimization: preset));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        // Two IComparable values go to the first list and one plain object to the second, as on desktop .NET.
        Assert.Equal(21, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    // Setter(Tuple<List<IComparable>, List<object>> lists, object value) in the XAML compiler's shape, and
    // Run(int) => calls it with "a", "b" and a plain object, then returns Item1.Count * 10 + Item2.Count.
    private static void WriteSetterProgram(string path)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("StackAcrossBranch"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("StackAcrossBranch.dll");
        var entry = module.DefineType("StackAcrossBranch.EntryPoint", TypeAttributes.Public);
        var listsType = typeof(Tuple<List<IComparable>, List<object>>);

        var setter = entry.DefineMethod("Setter", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [listsType, typeof(object)]);
        var code = setter.GetILGenerator();
        var comparable = code.DeclareLocal(typeof(IComparable));
        var other = code.DeclareLocal(typeof(object));
        var notComparable = code.DefineLabel();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Dup);
        code.Emit(OpCodes.Isinst, typeof(IComparable));
        code.Emit(OpCodes.Brfalse, notComparable);
        code.Emit(OpCodes.Stloc, comparable);
        code.Emit(OpCodes.Callvirt, listsType.GetProperty("Item1")!.GetGetMethod()!);
        code.Emit(OpCodes.Ldloc, comparable);
        code.Emit(OpCodes.Callvirt, typeof(List<IComparable>).GetMethod("Add")!);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(notComparable);
        code.Emit(OpCodes.Stloc, other);
        code.Emit(OpCodes.Callvirt, listsType.GetProperty("Item2")!.GetGetMethod()!);
        code.Emit(OpCodes.Ldloc, other);
        code.Emit(OpCodes.Callvirt, typeof(IList).GetMethod("Add")!);
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ret);

        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var body = run.GetILGenerator();
        var lists = body.DeclareLocal(listsType);
        body.Emit(OpCodes.Newobj, typeof(List<IComparable>).GetConstructor(Type.EmptyTypes)!);
        body.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        body.Emit(OpCodes.Newobj, listsType.GetConstructor([typeof(List<IComparable>), typeof(List<object>)])!);
        body.Emit(OpCodes.Stloc, lists);
        foreach (var argument in new[] { "a", "b" })
        {
            body.Emit(OpCodes.Ldloc, lists);
            body.Emit(OpCodes.Ldstr, argument);
            body.Emit(OpCodes.Call, setter);
        }
        body.Emit(OpCodes.Ldloc, lists);
        body.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
        body.Emit(OpCodes.Call, setter);
        body.Emit(OpCodes.Ldloc, lists);
        body.Emit(OpCodes.Callvirt, listsType.GetProperty("Item1")!.GetGetMethod()!);
        body.Emit(OpCodes.Callvirt, typeof(List<IComparable>).GetProperty("Count")!.GetGetMethod()!);
        body.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
        body.Emit(OpCodes.Mul);
        body.Emit(OpCodes.Ldloc, lists);
        body.Emit(OpCodes.Callvirt, listsType.GetProperty("Item2")!.GetGetMethod()!);
        body.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        body.Emit(OpCodes.Add);
        body.Emit(OpCodes.Ret);
        entry.CreateType();
        assembly.Save(path);
    }
}
