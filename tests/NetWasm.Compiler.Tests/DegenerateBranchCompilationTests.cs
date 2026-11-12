using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Control flow whose edges coincide or lead nowhere new: a branch, switch or leave to the next
// instruction, a switch whose targets repeat or that has none, such branches inside handlers and
// filters, a block that loops on itself and chains of them. Issue 221 was a conditional branch to
// its own fallthrough; each shape here runs as desktop .NET runs the same IL.
public sealed class DegenerateBranchCompilationTests
{
    private static readonly string[] Cases =
    [
        "BranchToNext", "SwitchAllToNext", "SwitchMixed", "SwitchNoTargets", "SwitchDuplicates", "LeaveToNext",
        "FallthroughInFinally", "FallthroughInCatch", "FallthroughInFilter", "SelfLoop", "FallthroughOnBackEdge",
        "ChainedFallthroughs", "SwitchBackToHeader", "LeaveWithoutRegions", "LeaveDiscardsTheStack",
        "LeaveFromCatchIntoTry", "LeaveFromCatchIntoNestedTryStart", "BranchToTryStart",
    ];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void DegenerateEdgesCompileAndRunAsDesktopDoes(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var image = Build();
        var assembly = Path.Combine(assets.Directory, $"DegenerateBranches{target}{optimization}.dll");
        File.WriteAllBytes(assembly, image);
        var desktop = Assembly.Load(image).GetType("DegenerateBranches.EntryPoint")!.GetMethod("Run")!;

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "DegenerateBranches.EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: CoreLibAlias, ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);

        for (var shape = 0; shape < Cases.Length; shape++)
        {
            foreach (var value in new[] { 0, 1, 2, 5 })
            {
                var input = shape * 16 + value;
                var expected = (int)desktop.Invoke(null, [input])!;
                var actual = CompilerTestSupport.ExecuteWithStandardWasiNode(
                    compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd);
                Assert.True(expected == actual, $"{Cases[shape]}({value}): desktop {expected}, NetWasm {actual}");
            }
        }
    }

    private static readonly ImmutableDictionary<string, string> CoreLibAlias =
        ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib");

    private static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("DegenerateBranches"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("DegenerateBranches.dll");
        var entry = module.DefineType("DegenerateBranches.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var shapes = Cases.Select(name => Define(entry, name)).ToArray();

        // Run(input): Cases[input / 16](input % 16).
        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var labels = shapes.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_S, (sbyte)16);
        code.Emit(OpCodes.Div);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < shapes.Length; index++)
        {
            code.MarkLabel(labels[index]);
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldc_I4_S, (sbyte)16);
            code.Emit(OpCodes.Rem);
            code.Emit(OpCodes.Call, shapes[index]);
            code.Emit(OpCodes.Ret);
        }

        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static MethodBuilder Define(TypeBuilder entry, string name)
    {
        var method = entry.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        switch (name)
        {
            case "BranchToNext":
            {
                var next = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Br, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "SwitchAllToNext":
            {
                var next = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Switch, new[] { next, next, next });
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "SwitchMixed":
            {
                var next = code.DefineLabel();
                var other = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Switch, new[] { next, other, next });
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
                code.Emit(OpCodes.Ret);
                code.MarkLabel(other);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)20);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "SwitchNoTargets":
                code.Emit(OpCodes.Ldarg_0);
                // ECMA-335 III.3.66 allows a switch of no targets; the label overload refuses one, so
                // the count is written directly.
                code.Emit(OpCodes.Switch, 0);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_7);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Ret);
                break;
            case "SwitchDuplicates":
            {
                var twice = code.DefineLabel();
                var once = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Switch, new[] { twice, twice, once, twice });
                code.Emit(OpCodes.Ldc_I4_0);
                code.Emit(OpCodes.Ret);
                code.MarkLabel(twice);
                code.Emit(OpCodes.Ldc_I4_2);
                code.Emit(OpCodes.Ret);
                code.MarkLabel(once);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "LeaveToNext":
            {
                // Inside the try, leave to the instruction after it, which is still in the try.
                var result = code.DeclareLocal(typeof(int));
                var next = code.DefineLabel();
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Stloc, result);
                code.Emit(OpCodes.Leave, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldc_I4_2);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Stloc, result);
                code.BeginFinallyBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "FallthroughInFinally":
            {
                var result = code.DeclareLocal(typeof(int));
                var next = code.DefineLabel();
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Stloc, result);
                code.BeginFinallyBlock();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Brtrue, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldc_I4_5);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "FallthroughInCatch":
            {
                var result = code.DeclareLocal(typeof(int));
                var next = code.DefineLabel();
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)100);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Div);
                code.Emit(OpCodes.Stloc, result);
                code.BeginCatchBlock(typeof(DivideByZeroException));
                code.Emit(OpCodes.Pop);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Brfalse, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldc_I4_M1);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "FallthroughInFilter":
            {
                var result = code.DeclareLocal(typeof(int));
                var next = code.DefineLabel();
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)60);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Div);
                code.Emit(OpCodes.Stloc, result);
                code.BeginExceptFilterBlock();
                code.Emit(OpCodes.Pop);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Brtrue, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldc_I4_1);
                code.BeginCatchBlock(null!);
                code.Emit(OpCodes.Pop);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)-7);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "SelfLoop":
            {
                // One block that loops on itself: count down to zero.
                var loop = code.DefineLabel();
                var count = code.DeclareLocal(typeof(int));
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Starg_S, (byte)0);
                code.MarkLabel(loop);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, count);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Sub);
                code.Emit(OpCodes.Dup);
                code.Emit(OpCodes.Starg_S, (byte)0);
                code.Emit(OpCodes.Brtrue, loop);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "FallthroughOnBackEdge":
            {
                var loop = code.DefineLabel();
                var next = code.DefineLabel();
                var done = code.DefineLabel();
                var sum = code.DeclareLocal(typeof(int));
                code.MarkLabel(loop);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Brfalse, done);
                code.Emit(OpCodes.Ldloc, sum);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, sum);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Sub);
                code.Emit(OpCodes.Starg_S, (byte)0);
                code.Emit(OpCodes.Ldloc, sum);
                code.Emit(OpCodes.Brtrue, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Br, loop);
                code.MarkLabel(done);
                code.Emit(OpCodes.Ldloc, sum);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "ChainedFallthroughs":
            {
                var first = code.DefineLabel();
                var second = code.DefineLabel();
                var third = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Brtrue, first);
                code.MarkLabel(first);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_2);
                code.Emit(OpCodes.Bgt, second);
                code.MarkLabel(second);
                code.Emit(OpCodes.Br, third);
                code.MarkLabel(third);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)11);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "LeaveWithoutRegions":
            {
                // A method without exception regions: leave is a branch that empties the stack.
                var next = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_4);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Starg_S, (byte)0);
                code.Emit(OpCodes.Leave, next);
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "LeaveDiscardsTheStack":
            {
                // leave empties the evaluation stack (ECMA-335 III.3.46), whatever is on it.
                var result = code.DeclareLocal(typeof(int));
                var done = code.DefineLabel();
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Stloc, result);
                code.Emit(OpCodes.Ldc_I4_7);
                code.Emit(OpCodes.Ldstr, "discarded");
                code.Emit(OpCodes.Leave, done);
                code.BeginFinallyBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.MarkLabel(done);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "LeaveFromCatchIntoTry":
            {
                // A catch may leave to any instruction of its own try block (ECMA-335 III.3.46):
                // a retry that divides again once the divisor is fixed.
                var divisor = code.DeclareLocal(typeof(int));
                var attempts = code.DeclareLocal(typeof(int));
                var result = code.DeclareLocal(typeof(int));
                var retry = code.DefineLabel();
                var done = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Stloc, divisor);
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldloc, attempts);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, attempts);
                code.MarkLabel(retry);
                code.Emit(OpCodes.Ldc_I4, 840);
                code.Emit(OpCodes.Ldloc, divisor);
                code.Emit(OpCodes.Div);
                code.Emit(OpCodes.Stloc, result);
                code.Emit(OpCodes.Leave, done);
                code.BeginCatchBlock(typeof(DivideByZeroException));
                code.Emit(OpCodes.Pop);
                code.Emit(OpCodes.Ldloc, divisor);
                code.Emit(OpCodes.Ldc_I4_7);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, divisor);
                code.Emit(OpCodes.Ldloc, attempts);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)100);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, attempts);
                code.Emit(OpCodes.Leave, retry);
                code.EndExceptionBlock();
                code.MarkLabel(done);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldloc, attempts);
                code.Emit(OpCodes.Ldc_I4, 1000);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "LeaveFromCatchIntoNestedTryStart":
            {
                // The target may begin a try nested in the catch's own try: entering a try at its
                // first instruction is allowed.
                var divisor = code.DeclareLocal(typeof(int));
                var result = code.DeclareLocal(typeof(int));
                var inner = code.DefineLabel();
                var done = code.DefineLabel();
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Stloc, divisor);
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Stloc, result);
                code.MarkLabel(inner);
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)90);
                code.Emit(OpCodes.Ldloc, divisor);
                code.Emit(OpCodes.Div);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, result);
                code.BeginFinallyBlock();
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, result);
                code.EndExceptionBlock();
                code.Emit(OpCodes.Leave, done);
                code.BeginCatchBlock(typeof(DivideByZeroException));
                code.Emit(OpCodes.Pop);
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.Stloc, divisor);
                code.Emit(OpCodes.Leave, inner);
                code.EndExceptionBlock();
                code.MarkLabel(done);
                code.Emit(OpCodes.Ldloc, result);
                code.Emit(OpCodes.Ret);
                break;
            }
            case "BranchToTryStart":
            {
                // A loop whose body is a try: its back edge branches to the try's first instruction
                // from outside it, as Roslyn emits for a loop around a try.
                var count = code.DeclareLocal(typeof(int));
                var top = code.DefineLabel();
                var after = code.DefineLabel();
                code.MarkLabel(top);
                code.BeginExceptionBlock();
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, count);
                code.BeginFinallyBlock();
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, count);
                code.EndExceptionBlock();
                code.MarkLabel(after);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)11);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Blt, top);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ret);
                break;
            }
            default:
            {
                // A switch whose targets include the loop's header and its own fallthrough.
                var header = code.DefineLabel();
                var next = code.DefineLabel();
                var count = code.DeclareLocal(typeof(int));
                code.MarkLabel(header);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
                code.Emit(OpCodes.Stloc, count);
                code.Emit(OpCodes.Ldarg_0);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Sub);
                code.Emit(OpCodes.Dup);
                code.Emit(OpCodes.Starg_S, (byte)0);
                code.Emit(OpCodes.Switch, new[] { next, header, header, next });
                code.MarkLabel(next);
                code.Emit(OpCodes.Ldloc, count);
                code.Emit(OpCodes.Ret);
                break;
            }
        }
        return method;
    }
}
