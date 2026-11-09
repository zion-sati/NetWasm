using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MemberwiseCloneCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClonePreservesRuntimeTypeFieldsAndShallowReferences(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "CloneFixture", Source, "CloneFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        namespace CloneFixture;
        public struct Pair { public object Reference; public long Number; }
        public class Base
        {
            private int _hidden = 17;
            public object Copy() => MemberwiseClone();
            public int Hidden => _hidden;
        }
        public sealed class Derived<T> : Base
        {
            public readonly T Item;
            public Pair Pair;
            public int Mutable;
            public Derived(T item) { Item = item; Mutable = 23; }
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var shared = new object();
                var source = new Derived<object>(shared);
                source.Pair = new Pair { Reference = source, Number = 1234567890123L };
                var copy = (Derived<object>)source.Copy();
                if (ReferenceEquals(source, copy) || copy.GetType() != source.GetType()) return 1;
                if (copy.Hidden != 17 || copy.Mutable != 23 || copy.Pair.Number != 1234567890123L) return 2;
                if (!ReferenceEquals(copy.Item, shared) || !ReferenceEquals(copy.Pair.Reference, source)) return 3;
                copy.Mutable = 99; if (source.Mutable != 23) return 4;
                GC.Collect();
                if (!ReferenceEquals(copy.Pair.Reference, source) || copy.Hidden != 17) return 5;
                var value = new Derived<long>(9876543210L);
                var valueCopy = (Derived<long>)value.Copy();
                if (valueCopy.Item != 9876543210L) return 6;
                return input + 1;
            }
        }
        """;
}
