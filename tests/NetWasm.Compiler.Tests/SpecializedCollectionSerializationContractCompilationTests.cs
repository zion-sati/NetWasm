using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SpecializedCollectionSerializationContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedCallbacksAndUnsupportedFormatterContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SpecializedSerializationFixture", Source, "SpecializedSerializationFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Specialized;
        using System.Runtime.Serialization;
        namespace SpecializedSerializationFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                var caught = 0;
                foreach (var value in new ISerializable[] { new PlainNames(), new NameValueCollection(), new OrderedDictionary() })
                {
                    try { value.GetObjectData(null!, default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("formatter")) return 2; caught++; }
                }
                try { new CustomNames(true); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { new CustomValues(true); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { new CustomOrdered(true); return 5; } catch (PlatformNotSupportedException) { caught++; }
                // Comparer serializes its CompareInfo, as upstream's does.
                try { ((ISerializable)Comparer.DefaultInvariant).GetObjectData(null!, default); return 7; } catch (ArgumentNullException) { caught++; }
                return caught == 7 ? input + 1 : 6;
            }
            public static int Portable(int input)
            {
                var names = new PlainNames(); names.Add("answer", 42);
                var values = new NameValueCollection(); values.Add("answer", "42");
                foreach (var value in new IDeserializationCallback[] { names, values })
                {
                    try { value.OnDeserialization(null); return 7; } catch (PlatformNotSupportedException) { }
                }
                if (!Equals(names.Get("answer"), 42) || values["answer"] != "42") return 8;
                var ordered = new OrderedDictionary(); ordered.Add("answer", 42);
                try { ((IDeserializationCallback)ordered).OnDeserialization(null); return 9; } catch (SerializationException) { }
                if (!Equals(ordered["answer"], 42)) return 10;
                var n = new CustomNames(); var v = new CustomValues(); var o = new CustomOrdered();
                foreach (var callback in new IDeserializationCallback[] { n, v, o }) callback.OnDeserialization(null);
                foreach (var serializer in new ISerializable[] { n, v, o }) serializer.GetObjectData(null!, default);
                if (n.Callbacks != 1 || v.Callbacks != 1 || o.Callbacks != 1 || n.Serializations != 1 || v.Serializations != 1 || o.Serializations != 1) return 11;
                if (Comparer.DefaultInvariant.Compare(1, 2) >= 0) return 12;
                return input + 1;
            }
        }
        public sealed class PlainNames : NameObjectCollectionBase
        {
            public void Add(string name, object value) => BaseAdd(name, value);
            public object? Get(string name) => BaseGet(name);
        }
        public sealed class CustomNames : NameObjectCollectionBase
        {
            public int Callbacks;
            public int Serializations;
            public CustomNames() { }
            public CustomNames(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) => Callbacks++;
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        public sealed class CustomValues : NameValueCollection
        {
            public int Callbacks;
            public int Serializations;
            public CustomValues() { }
            public CustomValues(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            public override void OnDeserialization(object? sender) => Callbacks++;
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        public sealed class CustomOrdered : OrderedDictionary
        {
            public int Callbacks;
            public int Serializations;
            public CustomOrdered() { }
            public CustomOrdered(bool formatter) : base((SerializationInfo)null!, default(StreamingContext)) { }
            protected override void OnDeserialization(object? sender) => Callbacks++;
            public override void GetObjectData(SerializationInfo info, StreamingContext context) => Serializations++;
        }
        """;
}
