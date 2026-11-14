using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ObjectTypeHandleCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ObjectHandlePreservesRuntimeTypeAndNullFailure(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ObjectTypeHandleFixture", Source, "ObjectTypeHandleFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        namespace ObjectTypeHandleFixture;
        public interface IValue { }
        public sealed class Value<T> : IValue { }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                IValue value = new Value<int>();
                object boxed = input;
                object array = new string[2];
                if (Type.GetTypeFromHandle(typeof(Value<int>).TypeHandle) != typeof(Value<int>)) return 7;
                if (Type.GetTypeFromHandle(Type.GetTypeHandle(value)) != typeof(Value<int>)) return 1;
                if (Type.GetTypeFromHandle(Type.GetTypeHandle(boxed)) != typeof(int)) return 2;
                if (Type.GetTypeFromHandle(Type.GetTypeHandle(array)) != typeof(string[])) return 3;
                if (Type.GetTypeFromHandle(Type.GetTypeHandle("text")) != typeof(string)) return 4;
                var first = Type.GetTypeHandle(value);
                var second = typeof(string).TypeHandle;
                if (first.Equals(second) || !first.Equals(typeof(Value<int>).TypeHandle)) return 8;
                var keys = new System.Collections.Generic.Dictionary<RuntimeTypeHandle, int>();
                keys.Add(first, 19);
                keys.Add(second, 23);
                if (keys.Count != 2 || keys[first] + keys[second] != 42) return 9;
                if (first.Value == 0 || first.Value == second.Value || default(RuntimeTypeHandle).Value != 0) return 10;
                if (first.GetHashCode() != typeof(Value<int>).TypeHandle.GetHashCode()) return 11;
                if (first.Equals((object)second) || !first.Equals((object)typeof(Value<int>).TypeHandle)) return 12;
                if (first.Equals(null) || first.Equals("unrelated") || first.Equals(default(RuntimeTypeHandle))) return 13;
                if (!((object)first == first) || !((object)first != second) || !(first == (object)first) || !(first != (object)second)) return 14;


                try { Type.GetTypeHandle(null); return 5; }
                catch (ArgumentNullException error) { return error.ParamName == "o" ? 42 : 6; }
            }
        }
        """;
}
