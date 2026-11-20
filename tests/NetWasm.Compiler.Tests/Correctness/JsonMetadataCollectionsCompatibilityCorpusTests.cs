// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// System.Text.Json's JsonMetadataServices factories and converters that the source generator emits for interface,
// concurrent, immutable, stack and queue collections, Memory<T>, and the DateOnly, TimeOnly, Half, Int128, UInt128,
// IEEE decimal, BFloat16, Memory<byte> and JsonDocument values. The fixture's resolver builds each JsonTypeInfo through
// JsonMetadataServices, as generated code does, and every input round-trips through JSON text.
public sealed class JsonMetadataCollectionsCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void GeneratedCollectionAndValueMetadataMatchesDesktop() =>
        runner.Run(new(
            "JsonMetadataCollectionsCompatibilityCorpus",
            "NetWasm.Correctness.JsonMetadataCollections",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            // The concurrent collections' contention paths wait on the platform's waitable set.
            RequiresReactor = true,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Numerics;
        using System.Text;
        using System.IO;
        using System.Runtime.InteropServices;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Text.Json.Serialization;
        using System.Text.Json.Serialization.Metadata;

        namespace NetWasm.Correctness.JsonMetadataCollections;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => ListInterfaces(),
                1 => Dictionaries(),
                2 => SetsStacksAndQueues(),
                3 => Immutables(),
                4 => Memories(),
                5 => DatesAndTimes(),
                6 => WideNumbers(),
                7 => Documents(),
                8 => ElementsAndDocuments(),
                9 => AsyncParsingAndRawValues(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static readonly JsonSerializerOptions s_options = new() { TypeInfoResolver = new PrimitiveResolver() };

            // As a generated context registering the primitives the weakly typed collections hold: writing an object
            // element looks up its runtime type's metadata here.
            private sealed class PrimitiveResolver : IJsonTypeInfoResolver
            {
                public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
                    type == typeof(int) ? JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter) :
                    type == typeof(string) ? JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter) :
                    type == typeof(bool) ? JsonMetadataServices.CreateValueInfo<bool>(options, JsonMetadataServices.BooleanConverter) :
                    type == typeof(object) ? JsonMetadataServices.CreateValueInfo<object>(options, JsonMetadataServices.ObjectConverter) :
                    null;
            }

            private static JsonTypeInfo<int> Int() => JsonMetadataServices.CreateValueInfo<int>(s_options, JsonMetadataServices.Int32Converter);
            private static JsonTypeInfo<string> Str() => JsonMetadataServices.CreateValueInfo<string>(s_options, JsonMetadataServices.StringConverter);
            private static JsonTypeInfo<object> Obj() => JsonMetadataServices.CreateValueInfo<object>(s_options, JsonMetadataServices.ObjectConverter);
            private static JsonTypeInfo<T> Value<T>(System.Text.Json.Serialization.JsonConverter<T> converter) =>
                JsonMetadataServices.CreateValueInfo<T>(s_options, converter);
            private static JsonCollectionInfoValues<T> Elements<T>(JsonTypeInfo element, JsonTypeInfo? key = null, Func<T>? create = null) =>
                new() { ElementInfo = element, KeyInfo = key, ObjectCreator = create };

            private static int Hash(string text)
            {
                var hash = 17;
                foreach (var c in text) hash = unchecked(hash * 31 + c);
                return hash & 0x7FFFFFFF;
            }

            private static int ListInterfaces()
            {
                var list = JsonMetadataServices.CreateIListInfo<IList<int>, int>(s_options, Elements<IList<int>>(Int()));
                var collection = JsonMetadataServices.CreateICollectionInfo<ICollection<int>, int>(s_options, Elements<ICollection<int>>(Int()));
                var enumerable = JsonMetadataServices.CreateIEnumerableInfo<IEnumerable<int>, int>(s_options, Elements<IEnumerable<int>>(Int()));
                var nonGeneric = JsonMetadataServices.CreateIListInfo<IList>(s_options, Elements<IList>(Obj()));
                var nonGenericEnumerable = JsonMetadataServices.CreateIEnumerableInfo<IEnumerable>(s_options, Elements<IEnumerable>(Obj()));

                var text = JsonSerializer.Serialize<IList<int>>(new List<int> { 1, 2, 3 }, list);
                var back = JsonSerializer.Deserialize("[4,5,6]", list)!;
                var fromCollection = JsonSerializer.Deserialize("[7,8]", collection)!;
                var fromEnumerable = JsonSerializer.Deserialize("[9]", enumerable)!;
                var weak = JsonSerializer.Deserialize("[1,\"a\",true]", nonGeneric)!;
                var weakEnumerable = JsonSerializer.Deserialize("[true]", nonGenericEnumerable)!;
                var sum = 0;
                foreach (var value in back) sum += value;
                foreach (var value in fromCollection) sum += value * 10;
                foreach (var value in fromEnumerable) sum += value * 100;
                var weakCount = 0;
                foreach (var _ in weakEnumerable) weakCount++;
                return Hash(text + "|" + weak.Count + "|" + weakCount) % 100000 + sum * 100000;
            }

            private static int Dictionaries()
            {
                var generic = JsonMetadataServices.CreateIDictionaryInfo<IDictionary<string, int>, string, int>(
                    s_options, Elements<IDictionary<string, int>>(Int(), Str()));
                var readOnly = JsonMetadataServices.CreateIReadOnlyDictionaryInfo<IReadOnlyDictionary<string, int>, string, int>(
                    s_options, Elements<IReadOnlyDictionary<string, int>>(Int(), Str()));
                var weak = JsonMetadataServices.CreateIDictionaryInfo<IDictionary>(
                    s_options, Elements<IDictionary>(Obj(), Str()));

                var read = JsonSerializer.Deserialize("{\"a\":1,\"b\":2}", generic)!;
                var readOnlyValue = JsonSerializer.Deserialize("{\"c\":3}", readOnly)!;
                var text = JsonSerializer.Serialize<IReadOnlyDictionary<string, int>>(new Dictionary<string, int> { ["x"] = 5, ["y"] = 6 }, readOnly);
                var weakRead = JsonSerializer.Deserialize("{\"k\":\"v\"}", weak)!;
                return Hash(text) % 100000 + (read["a"] + read["b"] * 10 + readOnlyValue["c"] * 100 + weakRead.Count * 1000) * 100000;
            }

            private static int SetsStacksAndQueues()
            {
                var set = JsonMetadataServices.CreateISetInfo<ISet<int>, int>(s_options, Elements<ISet<int>>(Int()));
                var readOnlySet = JsonMetadataServices.CreateIReadOnlySetInfo<IReadOnlySet<int>, int>(s_options, Elements<IReadOnlySet<int>>(Int()));
                var queue = JsonMetadataServices.CreateQueueInfo<Queue<int>, int>(s_options, Elements<Queue<int>>(Int(), create: static () => new Queue<int>()));
                var stack = JsonMetadataServices.CreateStackInfo<Stack<int>, int>(s_options, Elements<Stack<int>>(Int(), create: static () => new Stack<int>()));
                var concurrentQueue = JsonMetadataServices.CreateConcurrentQueueInfo<ConcurrentQueue<int>, int>(
                    s_options, Elements<ConcurrentQueue<int>>(Int(), create: static () => new ConcurrentQueue<int>()));
                var concurrentStack = JsonMetadataServices.CreateConcurrentStackInfo<ConcurrentStack<int>, int>(
                    s_options, Elements<ConcurrentStack<int>>(Int(), create: static () => new ConcurrentStack<int>()));
                var weakQueue = JsonMetadataServices.CreateQueueInfo<Queue>(
                    s_options, Elements<Queue>(Obj(), create: static () => new Queue()), static (queue, value) => queue.Enqueue(value));
                var weakStack = JsonMetadataServices.CreateStackInfo<Stack>(
                    s_options, Elements<Stack>(Obj(), create: static () => new Stack()), static (stack, value) => stack.Push(value));

                var setValue = JsonSerializer.Deserialize("[3,1,3,2]", set)!;
                var readOnlySetValue = JsonSerializer.Deserialize("[5,5]", readOnlySet)!;
                var q = JsonSerializer.Deserialize("[1,2,3]", queue)!;
                var s = JsonSerializer.Deserialize("[1,2,3]", stack)!;
                var cq = JsonSerializer.Deserialize("[4,5]", concurrentQueue)!;
                var cs = JsonSerializer.Deserialize("[4,5]", concurrentStack)!;
                var wq = JsonSerializer.Deserialize("[1,2]", weakQueue)!;
                var ws = JsonSerializer.Deserialize("[1,2]", weakStack)!;
                var text = JsonSerializer.Serialize(q, queue) + JsonSerializer.Serialize(s, stack) +
                    JsonSerializer.Serialize(cq, concurrentQueue) + JsonSerializer.Serialize(cs, concurrentStack) +
                    "|" + wq.Count + "|" + ws.Count;
                cs.TryPeek(out var top);
                return Hash(text) % 100000 + (setValue.Count + readOnlySetValue.Count * 10 + q.Peek() * 100 + s.Peek() * 1000 + top * 10000) * 100000 % 1000000000;
            }

            private static int Immutables()
            {
                var list = JsonMetadataServices.CreateImmutableEnumerableInfo<ImmutableList<int>, int>(
                    s_options, Elements<ImmutableList<int>>(Int()), ImmutableList.CreateRange);
                var array = JsonMetadataServices.CreateImmutableEnumerableInfo<ImmutableArray<int>, int>(
                    s_options, Elements<ImmutableArray<int>>(Int()), ImmutableArray.CreateRange);
                var dictionary = JsonMetadataServices.CreateImmutableDictionaryInfo<ImmutableDictionary<string, int>, string, int>(
                    s_options, Elements<ImmutableDictionary<string, int>>(Int(), Str()), ImmutableDictionary.CreateRange);

                var listValue = JsonSerializer.Deserialize("[1,2,3]", list)!;
                var arrayValue = JsonSerializer.Deserialize("[4,5]", array);
                var dictionaryValue = JsonSerializer.Deserialize("{\"a\":7}", dictionary)!;
                var text = JsonSerializer.Serialize(listValue.Add(9), list) + JsonSerializer.Serialize(arrayValue, array) +
                    JsonSerializer.Serialize(dictionaryValue, dictionary);
                return Hash(text) % 100000 + (listValue.Count + arrayValue[1] * 10 + dictionaryValue["a"] * 100) * 100000;
            }

            private static int Memories()
            {
                var memory = JsonMetadataServices.CreateMemoryInfo<int>(s_options, Elements<Memory<int>>(Int()));
                var readOnly = JsonMetadataServices.CreateReadOnlyMemoryInfo<int>(s_options, Elements<ReadOnlyMemory<int>>(Int()));
                var bytes = Value(JsonMetadataServices.MemoryByteConverter);
                var readOnlyBytes = Value(JsonMetadataServices.ReadOnlyMemoryByteConverter);

                var m = JsonSerializer.Deserialize("[1,2,3]", memory);
                var r = JsonSerializer.Deserialize("[4,5]", readOnly);
                var b = JsonSerializer.Deserialize("\"AQID\"", bytes);
                var text = JsonSerializer.Serialize(new Memory<int>(new[] { 7, 8 }), memory) +
                    JsonSerializer.Serialize(new ReadOnlyMemory<int>(new[] { 9 }), readOnly) +
                    JsonSerializer.Serialize(new Memory<byte>(new byte[] { 255, 0, 1 }), bytes) +
                    JsonSerializer.Serialize(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("hi")), readOnlyBytes);
                return Hash(text) % 100000 + (m.Span[2] + r.Span[0] * 10 + b.Length * 100 + b.Span[2] * 1000) * 100000;
            }

            private static int DatesAndTimes()
            {
                var date = Value(JsonMetadataServices.DateOnlyConverter);
                var time = Value(JsonMetadataServices.TimeOnlyConverter);
                var d = JsonSerializer.Deserialize("\"2024-02-29\"", date);
                var t = JsonSerializer.Deserialize("\"13:45:30.1234567\"", time);
                var text = JsonSerializer.Serialize(new DateOnly(1999, 12, 31), date) + JsonSerializer.Serialize(new TimeOnly(1, 2, 3, 4), time);
                var invalid = 0;
                try
                {
                    JsonSerializer.Deserialize("\"2024-13-01\"", date);
                }
                catch (JsonException)
                {
                    invalid = 1;
                }
                return Hash(text) % 100000 + (d.Day + t.Minute * 100 + invalid * 10000) * 100000 % 1000000000;
            }

            private static int WideNumbers()
            {
                var half = Value(JsonMetadataServices.HalfConverter);
                var int128 = Value(JsonMetadataServices.Int128Converter);
                var uint128 = Value(JsonMetadataServices.UInt128Converter);
                var bfloat = Value(JsonMetadataServices.BFloat16Converter);
                var d32 = Value(JsonMetadataServices.Decimal32Converter);
                var d64 = Value(JsonMetadataServices.Decimal64Converter);
                var d128 = Value(JsonMetadataServices.Decimal128Converter);

                var text = JsonSerializer.Serialize((Half)1.5f, half) + "|" +
                    JsonSerializer.Serialize(Int128.MinValue, int128) + "|" +
                    JsonSerializer.Serialize(UInt128.MaxValue, uint128) + "|" +
                    JsonSerializer.Serialize((BFloat16)2.5f, bfloat) + "|" +
                    JsonSerializer.Serialize(JsonSerializer.Deserialize("1.25", d32), d32) + "|" +
                    JsonSerializer.Serialize(JsonSerializer.Deserialize("-0.001", d64), d64) + "|" +
                    JsonSerializer.Serialize(JsonSerializer.Deserialize("12345678901234567890.5", d128), d128);
                var big = JsonSerializer.Deserialize("170141183460469231731687303715884105727", int128);
                var small = JsonSerializer.Deserialize("-3", int128);
                var unsigned = JsonSerializer.Deserialize("18446744073709551616", uint128);
                var h = JsonSerializer.Deserialize("-0.25", half);
                var outOfRange = 0;
                try
                {
                    JsonSerializer.Deserialize("-1", uint128);
                }
                catch (JsonException)
                {
                    outOfRange = 1;
                }
                return Hash(text) % 100000 + ((big == Int128.MaxValue ? 1 : 0) + (small == -3 ? 10 : 0) +
                    (unsigned == (UInt128)ulong.MaxValue + 1 ? 100 : 0) + ((float)h == -0.25f ? 1000 : 0) + outOfRange * 10000) * 100000;
            }

            private static int Documents()
            {
                var document = Value(JsonMetadataServices.JsonDocumentConverter);
                using var parsed = JsonSerializer.Deserialize("{\"a\":[1,2,{\"b\":null}]}", document)!;
                var text = JsonSerializer.Serialize(parsed, document);
                var length = parsed.RootElement.GetProperty("a").GetArrayLength();
                return Hash(text) % 100000 + length * 100000;
            }
            public enum Color { Red = 1, Green = 2 }

            private static int ElementsAndDocuments()
            {
                var list = JsonMetadataServices.CreateIListInfo<IList<int>, int>(s_options, Elements<IList<int>>(Int()));
                var element = JsonSerializer.SerializeToElement<IList<int>>(new List<int> { 3, 4 }, list);
                using var document = JsonSerializer.SerializeToDocument<IList<int>>(new List<int> { 5 }, list);
                var fromElement = JsonSerializer.Deserialize(element, list)!;
                var fromDocument = JsonSerializer.Deserialize(document, list)!;
                var untyped = JsonSerializer.SerializeToElement((object)42, (JsonTypeInfo)Int());
                var back = (int)JsonSerializer.Deserialize(untyped, (JsonTypeInfo)Int())!;
                return Hash(element.GetRawText() + "|" + document.RootElement.GetRawText() + "|" + untyped.GetRawText()) % 100000 +
                    (fromElement[1] + fromDocument[0] * 10 + back * 100) * 100000 % 1000000000;
            }

            private static int AsyncParsingAndRawValues()
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\": [1, 2.50, \"x\"]}"));
                using var document = JsonDocument.ParseAsync(stream).GetAwaiter().GetResult();
                var raw = Encoding.UTF8.GetString(JsonMarshal.GetRawUtf8Value(document.RootElement.GetProperty("a")[1]));
                var node = JsonNode.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes("[true, {\"b\": 3}]"))).GetAwaiter().GetResult()!;
                var b = node[1]!["b"]!.GetValue<int>();

                var numbers = (JsonConverter<Color>)new JsonNumberEnumConverter<Color>().CreateConverter(typeof(Color), s_options)!;
                var color = Value(numbers);
                var text = JsonSerializer.Serialize(Color.Green, color);
                var read = JsonSerializer.Deserialize("1", color);
                var rejected = 0;
                try
                {
                    JsonSerializer.Deserialize("\"Red\"", color);
                }
                catch (JsonException)
                {
                    rejected = 1;
                }
                return Hash(raw + "|" + text) % 100000 + (b + (int)read * 10 + rejected * 100) * 100000;
            }
        }
        """;
}
