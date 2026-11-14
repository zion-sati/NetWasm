using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeHelperContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CanonicalTypeHelpersMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("TypeHelperFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeHelperFixture.EntryPoint",
            "Run", [new("run", "TypeHelperFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ByReferenceTypeHasElementType(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ByReferenceTypeHelper"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ByReferenceTypeHelper.dll");
        var entry = module.DefineType("ByReferenceTypeHelper.EntryPoint", TypeAttributes.Public);
        var referenced = entry.DefineMethod("Accept", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(int).MakeByRefType()]);
        referenced.GetILGenerator().Emit(OpCodes.Ret);
        var method = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Ldtoken, referenced);
        code.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), [typeof(RuntimeMethodHandle)])!);
        code.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(nameof(MethodBase.GetParameters))!);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldelem_Ref);
        code.Emit(OpCodes.Callvirt, typeof(ParameterInfo).GetProperty(nameof(ParameterInfo.ParameterType))!.GetMethod!);
        code.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.HasElementType))!.GetMethod!);
        code.Emit(OpCodes.Ret);
        entry.CreateType();
        var path = Path.Combine(assets.Directory, "ByReferenceTypeHelper.dll");
        assembly.Save(path);
        var result = NetWasmCompiler.Compile(new(path, [assets.CoreLib], "ByReferenceTypeHelper.EntryPoint",
            "Run", [new("run", "ByReferenceTypeHelper.EntryPoint", "Run")], Target: target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(1, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        namespace TypeHelperFixture;
        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                if (Type.Delimiter != '.') return 1;
                if (!typeof(int).Equals(typeof(int)) || typeof(int).Equals(typeof(string)) || typeof(int).Equals((Type)null)) return 2;
                var values = new object[] { 42, "text", Small.One, new int[1], new Pair<int, string>() };
                var types = Type.GetTypeArray(values);
                if (types.Length != 5 || types[0] != typeof(int) || types[1] != typeof(string) ||
                    types[2] != typeof(Small) || types[3] != typeof(int[]) || types[4] != typeof(Pair<int, string>)) return 3;
                types[0] = typeof(string);
                if (Type.GetTypeArray(values)[0] != typeof(int) || Type.GetTypeArray(new object[0]).Length != 0) return 4;
                var caught = 0;
                try { Type.GetTypeArray(null); } catch (ArgumentNullException e) { if (e.ParamName == "args") caught++; }
                try { Type.GetTypeArray(new object[] { 1, null }); } catch (ArgumentException e) { if (e.ParamName == "args") caught++; }
                if (caught != 2) return 5;

                if (typeof(int).IsConstructedGenericType || typeof(Pair<,>).IsConstructedGenericType ||
                    !typeof(Pair<int, string>).IsConstructedGenericType) return 6;
                if (typeof(int).GenericTypeArguments.Length != 0 || typeof(Pair<,>).GenericTypeArguments.Length != 0) return 7;
                var arguments = typeof(Pair<int, string>).GenericTypeArguments;
                if (arguments.Length != 2 || arguments[0] != typeof(int) || arguments[1] != typeof(string)) return 8;
                arguments[0] = typeof(string);
                if (typeof(Pair<int, string>).GenericTypeArguments[0] != typeof(int)) return 9;

                if (typeof(int).HasElementType || !typeof(int[]).HasElementType || !typeof(int[,]).HasElementType || !typeof(int*).HasElementType) return 10;
                if (typeof(int).IsVariableBoundArray || typeof(int[]).IsVariableBoundArray || !typeof(int[,]).IsVariableBoundArray) return 11;
                return input + 1;
            }
            public static int Increment(int value) => value + 1;
            private sealed class Pair<T, U> { }
            private enum Small : byte { One = 1 }
        }
        """;
}
