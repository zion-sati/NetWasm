using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Invalid stacks on a path a constant branch never takes are still invalid IL: the
// specializer validates the original method before any rule folds that path away.
public sealed class FoldedInvalidStackCompilationTests
{
    [Fact]
    public void AnInt32AddedToAFloatOnAPathNeverTakenIsRejected()
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "FoldedInvalidStack.dll");
        File.WriteAllBytes(assembly, Build());

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add(
                "System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory)));

        Assert.Equal(DiagnosticCode.InvalidCil, exception.Diagnostic.Code);
        Assert.Contains("numeric operands are incompatible", exception.Message);
    }

    private static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("FoldedInvalidStack"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("FoldedInvalidStack.dll");
        var type = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var never = code.DefineLabel();
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Brtrue, never);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(never);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_R8, 2.0);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Conv_I4);
        code.Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
