using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeElementContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ElementTypesComeFromTheTypeTable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("TypeElementFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeElementFixture.EntryPoint",
            "Run", [new("run", "TypeElementFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ByReferenceElementTypeComesFromTheTypeTable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ByReferenceElementContract"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ByReferenceElementContract.dll");
        var entry = module.DefineType("ByReferenceElementContract.EntryPoint", TypeAttributes.Public);
        var referenced = entry.DefineMethod("Accept", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(int).MakeByRefType()]);
        referenced.GetILGenerator().Emit(OpCodes.Ret);
        var method = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        var outcome = code.DeclareLocal(typeof(int));
        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldtoken, referenced);
        code.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), [typeof(RuntimeMethodHandle)])!);
        code.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(nameof(MethodBase.GetParameters))!);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldelem_Ref);
        code.Emit(OpCodes.Callvirt, typeof(ParameterInfo).GetProperty(nameof(ParameterInfo.ParameterType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetMethod(nameof(Type.GetElementType))!);
        // The type table answers a by-reference type's element: int& reads as int.
        code.Emit(OpCodes.Ldtoken, typeof(int));
        code.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
        code.Emit(OpCodes.Call, typeof(Type).GetMethod("op_Equality", [typeof(Type), typeof(Type)])!);
        code.Emit(OpCodes.Stloc, outcome);
        code.BeginCatchBlock(typeof(PlatformNotSupportedException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_2);
        code.Emit(OpCodes.Stloc, outcome);
        code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, outcome);
        code.Emit(OpCodes.Ret);
        entry.CreateType();
        var path = Path.Combine(assets.Directory, "ByReferenceElementContract.dll");
        assembly.Save(path);
        var result = NetWasmCompiler.Compile(new(path, [assets.CoreLib], "ByReferenceElementContract.EntryPoint",
            "Run", [new("run", "ByReferenceElementContract.EntryPoint", "Run")], Target: target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(1, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeElementFixture;
        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                foreach (var type in new Type[] { typeof(int), typeof(string), typeof(Box<>), typeof(Box<int>), typeof(Box<>).GetGenericArguments()[0] })
                    if (type.GetElementType() != null) return 1;
                // Arrays, jagged arrays and pointers answer their element from the type table.
                if (typeof(int[]).GetElementType() != typeof(int) || typeof(int[,]).GetElementType() != typeof(int) ||
                    typeof(int[][]).GetElementType() != typeof(int[]) || typeof(int*).GetElementType() != typeof(int) ||
                    typeof(Box<int>[]).GetElementType() != typeof(Box<int>)) return 2;
                return input + 1;
            }
            private class Box<T> { }
        }
        """;
}
