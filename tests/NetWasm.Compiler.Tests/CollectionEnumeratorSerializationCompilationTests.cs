using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CollectionEnumeratorSerializationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedCallbacksAndUnsupportedFormatterContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EnumeratorSerializationFixture", Source, "EnumeratorSerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.Serialization;
        namespace EnumeratorSerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var list = new LinkedList<int>(); list.AddLast(20); list.AddLast(22);
                var set = new SortedSet<int> { 20, 22 };
                var listEnumerator = list.GetEnumerator();
                var setEnumerator = set.GetEnumerator();
                if (!listEnumerator.MoveNext() || !setEnumerator.MoveNext()) return 1;
                if (listEnumerator.Current != 20 || setEnumerator.Current != 20) return 2;
                var count = 0;
                foreach (var value in new object[] { listEnumerator, setEnumerator, default(LinkedList<int>.Enumerator), default(SortedSet<int>.Enumerator) })
                {
                    if (value is not ISerializable serialization || value is not IDeserializationCallback callback) return 3;
                    try { serialization.GetObjectData(null!, default); return 4; } catch (PlatformNotSupportedException) { count++; }
                    try { callback.OnDeserialization(null); return 5; } catch (PlatformNotSupportedException) { count++; }
                }
                if (!listEnumerator.MoveNext() || !setEnumerator.MoveNext()) return 6;
                if (listEnumerator.Current != 22 || setEnumerator.Current != 22) return 7;
                if (listEnumerator.MoveNext() || setEnumerator.MoveNext()) return 8;
                listEnumerator.Dispose(); setEnumerator.Dispose();
                return count == 8 ? input + 1 : 9;
            }
        }
        """;
}
