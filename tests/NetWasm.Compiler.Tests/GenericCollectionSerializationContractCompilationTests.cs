using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericCollectionSerializationContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedCallbacksAndUnsupportedFormatterContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "CollectionSerializationFixture", Source, "CollectionSerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.Serialization;
        namespace CollectionSerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                var caught = 0;
                foreach (var value in new ISerializable[] { new Dictionary<int, int>(), new HashSet<int>(), new LinkedList<int>() })
                {
                    try { value.GetObjectData(null!, default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("formatter")) return 2; caught++; }
                }
                try { new DerivedDictionary(true); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { new DerivedSet(true); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { new DerivedList(true); return 5; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 6 ? input + 1 : 6;
            }
            public static int Portable(int input)
            {
                var dictionary = new Dictionary<int, int> { [1] = 42 };
                var set = new HashSet<int> { 42 };
                var list = new LinkedList<int>(); list.AddLast(42);
                foreach (var value in new IDeserializationCallback[] { dictionary, set, list }) { value.OnDeserialization(null); value.OnDeserialization(new object()); }
                if (dictionary[1] != 42 || dictionary.Count != 1 || !set.Contains(42) || set.Count != 1 || list.First!.Value != 42 || list.Count != 1) return 7;
                var d = new DerivedDictionary(); var h = new DerivedSet(); var l = new DerivedList();
                foreach (var value in new IDeserializationCallback[] { d, h, l }) value.OnDeserialization(null);
                d.OnDeserialization(null); h.OnDeserialization(null); l.OnDeserialization(null);
                foreach (var value in new ISerializable[] { d, h, l }) value.GetObjectData(null!, default);
                if (d.Callbacks != 2 || h.Callbacks != 2 || l.Callbacks != 2 || d.Serializations != 1 || h.Serializations != 1 || l.Serializations != 1) return 8;
                return input + 1;
            }
        }
        public sealed class DerivedDictionary : Dictionary<int, int>
        {
            public int Callbacks;
            public int Serializations;
            public DerivedDictionary() { }
            public DerivedDictionary(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) { Callbacks++; base.OnDeserialization(sender); }
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        public sealed class DerivedSet : HashSet<int>
        {
            public int Callbacks;
            public int Serializations;
            public DerivedSet() { }
            public DerivedSet(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) { Callbacks++; base.OnDeserialization(sender); }
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        public sealed class DerivedList : LinkedList<int>
        {
            public int Callbacks;
            public int Serializations;
            public DerivedList() { }
            public DerivedList(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) { Callbacks++; base.OnDeserialization(sender); }
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        """;
}
