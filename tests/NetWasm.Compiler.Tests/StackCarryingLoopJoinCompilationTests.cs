// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Loops entered through a jump to their test, whose code moves ahead of that jump at the speed and size levels (loop
// invariant expressions and calls) or merges across joins (selects), each run against desktop .NET: a branch that
// ends on the test where the entry jump lands, a join whose arms each push a value the join pops, and a choice of
// invariant values in the test itself, each in plain code and inside a try/finally.
public sealed class StackCarryingLoopJoinCompilationTests
{
    private static readonly string[] Methods =
    [
        "EndsAtTest", "EndsAtTestProtected", "JoinPops", "JoinPopsProtected", "ChoiceInTest", "ChoiceInTestProtected",
    ];

    private static readonly FinalWasmOptimization[] Levels =
    [
        FinalWasmOptimization.O1, FinalWasmOptimization.O3, FinalWasmOptimization.Os, FinalWasmOptimization.Oz,
    ];

    public static TheoryData<string, WasmTarget, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<string, WasmTarget, FinalWasmOptimization>();
        foreach (var method in Methods)
        {
            foreach (var level in Levels)
            {
                cases.Add(method, WasmTarget.Wasm32, level);
                cases.Add(method, WasmTarget.Wasm64, level);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachLoopMatchesDesktop(string method, WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var path = Build(assets);
        var expected = Desktop(path, method);

        var result = NetWasmCompiler.Compile(new(path, [assets.CoreLib], "StackJoins.EntryPoint", method, [],
            Target: target, Optimization: optimization,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib")));

        foreach (var (input, value) in expected)
        {
            Assert.Equal(value, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory,
                input, target, result.StaticDataEnd));
        }
    }

    private static readonly int[] Inputs = [0, 7];

    private static ImmutableArray<(int Input, int Value)> Desktop(string path, string method)
    {
        var context = new AssemblyLoadContext("StackJoins.Desktop", isCollectible: true);
        try
        {
            using var stream = File.OpenRead(path);
            var run = context.LoadFromStream(stream).GetType("StackJoins.EntryPoint")!.GetMethod(method)!;
            return [.. Inputs.Select(input => (input, (int)run.Invoke(null, [input])!))];
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Build(TestAssets assets)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("StackJoins"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("StackJoins.dll");
        var entry = module.DefineType("StackJoins.EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        Define(entry, "EndsAtTest", protect: false, EndsAtTest);
        Define(entry, "EndsAtTestProtected", protect: true, EndsAtTest);
        Define(entry, "JoinPops", protect: false, JoinPops);
        Define(entry, "JoinPopsProtected", protect: true, JoinPops);
        Define(entry, "ChoiceInTest", protect: false, ChoiceInTest);
        Define(entry, "ChoiceInTestProtected", protect: true, ChoiceInTest);
        entry.CreateType();
        var path = Path.Combine(assets.Directory, "StackJoins.dll");
        assembly.Save(path);
        return path;
    }

    // static int name(int a) { int i = 0, sum = 0; [try {] loop [} finally { a / 1 }] return sum; }
    private static void Define(TypeBuilder type, string name, bool protect, Action<ILGenerator> loop)
    {
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        code.DeclareLocal(typeof(int));
        code.DeclareLocal(typeof(int));
        code.Emit(OpCodes.Ldc_I4_0); code.Emit(OpCodes.Stloc_0);
        code.Emit(OpCodes.Ldc_I4_0); code.Emit(OpCodes.Stloc_1);
        if (protect)
        {
            code.BeginExceptionBlock();
        }
        loop(code);
        if (protect)
        {
            code.BeginFinallyBlock();
            code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldc_I4_1); code.Emit(OpCodes.Div); code.Emit(OpCodes.Pop);
            code.EndExceptionBlock();
        }
        code.Emit(OpCodes.Ldloc_1);
        code.Emit(OpCodes.Ret);
    }

    // goto test; body: i++; if (a != 0) goto test; test: sum += a; if (i < 3) goto body. The branch's both ways land on
    // the test, so `ldarg.0; brtrue test; test: ldarg.0` leaves one value while the entry jump lands on its last
    // instruction. The test runs four times.
    private static void EndsAtTest(ILGenerator code)
    {
        var body = code.DefineLabel();
        var test = code.DefineLabel();
        code.Emit(OpCodes.Br, test);
        code.MarkLabel(body);
        code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Ldc_I4_1); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_0);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Brtrue, test);
        code.MarkLabel(test);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldloc_1); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_1);
        code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Ldc_I4_3); code.Emit(OpCodes.Blt, body);
    }

    // goto test; body: i++; push (a != 0 ? 2 : 1) through two arms; the join pops it; sum += a + i; test: if (i < 3)
    // goto body.
    private static void JoinPops(ILGenerator code)
    {
        var body = code.DefineLabel();
        var test = code.DefineLabel();
        var second = code.DefineLabel();
        var join = code.DefineLabel();
        code.Emit(OpCodes.Br, test);
        code.MarkLabel(body);
        code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Ldc_I4_1); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_0);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Brtrue, second);
        code.Emit(OpCodes.Ldc_I4_1); code.Emit(OpCodes.Br, join);
        code.MarkLabel(second);
        code.Emit(OpCodes.Ldc_I4_2);
        code.MarkLabel(join);
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldloc_1); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_1);
        code.MarkLabel(test);
        code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Ldc_I4_3); code.Emit(OpCodes.Blt, body);
    }

    // goto test; body: i++; sum += i; test: if (i < (a > 5 ? a : 5)) goto body. The choice is invariant and branches,
    // so it may move ahead of the entry jump, which lands on the test's first instruction, before the choice.
    private static void ChoiceInTest(ILGenerator code)
    {
        var body = code.DefineLabel();
        var test = code.DefineLabel();
        var large = code.DefineLabel();
        var chosen = code.DefineLabel();
        code.Emit(OpCodes.Br, test);
        code.MarkLabel(body);
        code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Ldc_I4_1); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_0);
        code.Emit(OpCodes.Ldloc_1); code.Emit(OpCodes.Ldloc_0); code.Emit(OpCodes.Add); code.Emit(OpCodes.Stloc_1);
        code.MarkLabel(test);
        code.Emit(OpCodes.Ldloc_0);
        code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldc_I4_5); code.Emit(OpCodes.Bgt, large);
        code.Emit(OpCodes.Ldc_I4_5); code.Emit(OpCodes.Br, chosen);
        code.MarkLabel(large);
        code.Emit(OpCodes.Ldarg_0);
        code.MarkLabel(chosen);
        code.Emit(OpCodes.Blt, body);
    }
}
