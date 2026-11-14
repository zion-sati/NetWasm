using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A conditional branch whose target is the block after it has one successor: both outcomes continue there. Valid IL
// has them (SharpCompress's BZip2 encoder, once NetWasm's rewrites empty the code a branch skipped), and structuring
// read a second successor that was not there. The branch still consumes its operands.
public sealed class BranchToNextBlockCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AConditionalBranchToTheNextBlockConsumesItsOperandsAndContinues(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("BranchToNextBlock"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("BranchToNextBlock.dll");
        var entry = module.DefineType("BranchToNextBlock.EntryPoint", TypeAttributes.Public);
        var method = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        var counter = code.DeclareLocal(typeof(int));
        var afterCompare = code.DefineLabel();
        var afterTest = code.DefineLabel();
        var loop = code.DefineLabel();
        var insideLoop = code.DefineLabel();
        // Two-operand compare to the next block.
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_5);
        code.Emit(OpCodes.Ble_S, afterCompare);
        code.MarkLabel(afterCompare);
        // One-operand test to the next block.
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Brtrue_S, afterTest);
        code.MarkLabel(afterTest);
        // The same inside a loop: counter runs 0..9.
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Stloc, counter);
        code.MarkLabel(loop);
        code.Emit(OpCodes.Ldloc, counter);
        code.Emit(OpCodes.Ldc_I4_3);
        code.Emit(OpCodes.Bge_S, insideLoop);
        code.MarkLabel(insideLoop);
        code.Emit(OpCodes.Ldloc, counter);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, counter);
        code.Emit(OpCodes.Ldloc, counter);
        code.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
        code.Emit(OpCodes.Blt_S, loop);
        // input + counter - 9 = input + 1.
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldloc, counter);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldc_I4_S, (sbyte)9);
        code.Emit(OpCodes.Sub);
        code.Emit(OpCodes.Ret);
        entry.CreateType();
        var path = Path.Combine(assets.Directory, "BranchToNextBlock.dll");
        assembly.Save(path);
        var result = NetWasmCompiler.Compile(new(path, [assets.CoreLib], "BranchToNextBlock.EntryPoint",
            "Run", [new("run", "BranchToNextBlock.EntryPoint", "Run")], Target: target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }
}
