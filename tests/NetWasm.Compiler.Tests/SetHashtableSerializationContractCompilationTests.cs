using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SetHashtableSerializationContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedCallbacksAndUnsupportedFormatterContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SetHashtableSerializationFixture", Source, "SetHashtableSerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Runtime.Serialization;
        namespace SetHashtableSerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var caught = 0;
                foreach (var value in new ISerializable[] { new SortedSet<int>(), new Hashtable() })
                {
                    try { value.GetObjectData(null!, default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("formatter")) return 2; caught++; }
                }
                try { new CustomSet(true); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { new CustomTable(true); return 4; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 4 ? input + 1 : 5;
            }
            public static int Portable(int input)
            {
                var set = new SortedSet<int> { 1, 42, 100 };
                var table = new Hashtable { ["answer"] = 42 };
                foreach (var callback in new IDeserializationCallback[] { set, table }) { callback.OnDeserialization(null); callback.OnDeserialization(new object()); }
                if (set.Count != 3 || !set.Contains(42) || !Equals(table["answer"], 42)) return 6;
                var view = set.GetViewBetween(1, 42);
                try { ((IDeserializationCallback)view).OnDeserialization(null); return 7; } catch (PlatformNotSupportedException) { }
                try { ((ISerializable)view).GetObjectData(null!, default); return 8; } catch (PlatformNotSupportedException) { }
                if (view.Count != 2 || view.Max != 42) return 9;
                var customSet = new CustomSet(); var customTable = new CustomTable();
                foreach (var callback in new IDeserializationCallback[] { customSet, customTable }) callback.OnDeserialization(null);
                foreach (var serializer in new ISerializable[] { customSet, customTable }) serializer.GetObjectData(null!, default);
                if (customSet.Callbacks != 1 || customTable.Callbacks != 1 || customSet.Serializations != 1 || customTable.Serializations != 1) return 10;
                customTable["answer"] = 42;
                var sync = Hashtable.Synchronized(customTable);
                ((IDeserializationCallback)sync).OnDeserialization(null);
                if (customTable.Callbacks != 1 || !Equals(sync["answer"], 42)) return 11;
                try { ((ISerializable)sync).GetObjectData(null!, default); return 12; } catch (PlatformNotSupportedException) { }
                if (customTable.Serializations != 1) return 13;
                return input + 1;
            }
        }
        public sealed class CustomSet : SortedSet<int>
        {
            public int Callbacks;
            public int Serializations;
            public CustomSet() { }
            public CustomSet(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            protected override void OnDeserialization(object? sender) { Callbacks++; base.OnDeserialization(sender); }
            protected override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        public sealed class CustomTable : Hashtable
        {
            public int Callbacks;
            public int Serializations;
            public CustomTable() { }
            public CustomTable(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) { Callbacks++; base.OnDeserialization(sender); }
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        """;
}
