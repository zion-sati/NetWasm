// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// System.Text.Json's polymorphic serialization through source-generated metadata: an object-typed value is written
// through its runtime type's metadata, which the resolver must supply (desktop throws NotSupportedException otherwise),
// and a type with PolymorphismOptions writes and reads its derived types with their discriminators. The port wrote
// every object-typed value as {} and ignored derived types. The fixture's resolver builds metadata through
// JsonMetadataServices, as generated code does.
public sealed class JsonPolymorphismCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PolymorphicValuesMatchDesktop() =>
        runner.Run(new(
            "JsonPolymorphismCompatibilityCorpus",
            "NetWasm.Correctness.JsonPolymorphism",
            Source,
            [0, 1, 2, 3, 4, 5])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ExpectedReturnValue = 100,
        });

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization.Metadata;

        namespace NetWasm.Correctness.JsonPolymorphism;

        public abstract class Animal
        {
            public string Name { get; set; } = "";
        }

        public sealed class Dog : Animal
        {
            public int Barks { get; set; }
        }

        public sealed class Cat : Animal
        {
            public bool Indoor { get; set; }
        }

        public static class EntryPoint
        {
            private sealed class Resolver : IJsonTypeInfoResolver
            {
                public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
                {
                    if (type == typeof(int)) return JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter);
                    if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);
                    if (type == typeof(bool)) return JsonMetadataServices.CreateValueInfo<bool>(options, JsonMetadataServices.BooleanConverter);
                    if (type == typeof(object)) return JsonMetadataServices.CreateValueInfo<object>(options, JsonMetadataServices.ObjectConverter);
                    if (type == typeof(List<object>))
                        return JsonMetadataServices.CreateListInfo<List<object>, object>(options, new() { ObjectCreator = static () => new List<object>(), ElementInfo = Info<object>(options) });
                    if (type == typeof(Dictionary<string, object>))
                        return JsonMetadataServices.CreateDictionaryInfo<Dictionary<string, object>, string, object>(options,
                            new() { ObjectCreator = static () => new Dictionary<string, object>(), KeyInfo = Info<string>(options), ElementInfo = Info<object>(options) });
                    if (type == typeof(List<Animal>))
                        return JsonMetadataServices.CreateListInfo<List<Animal>, Animal>(options, new() { ObjectCreator = static () => new List<Animal>(), ElementInfo = Info<Animal>(options) });
                    if (type == typeof(Animal))
                    {
                        var info = JsonMetadataServices.CreateObjectInfo<Animal>(options, new()
                        {
                            PropertyMetadataInitializer = _ => new[] { NameProperty<Animal>(options) },
                        });
                        info.PolymorphismOptions = new JsonPolymorphismOptions
                        {
                            DerivedTypes = { new JsonDerivedType(typeof(Dog), "dog"), new JsonDerivedType(typeof(Cat), "cat") },
                        };
                        return info;
                    }
                    if (type == typeof(Dog))
                        return JsonMetadataServices.CreateObjectInfo<Dog>(options, new()
                        {
                            ObjectCreator = static () => new Dog(),
                            PropertyMetadataInitializer = _ => new[]
                            {
                                NameProperty<Dog>(options),
                                JsonMetadataServices.CreatePropertyInfo<int>(options, new()
                                {
                                    IsProperty = true, IsPublic = true, DeclaringType = typeof(Dog), PropertyName = "Barks",
                                    PropertyTypeInfo = Info<int>(options),
                                    Getter = static o => ((Dog)o).Barks, Setter = static (o, v) => ((Dog)o).Barks = v,
                                }),
                            },
                        });
                    if (type == typeof(Cat))
                        return JsonMetadataServices.CreateObjectInfo<Cat>(options, new()
                        {
                            ObjectCreator = static () => new Cat(),
                            PropertyMetadataInitializer = _ => new[]
                            {
                                NameProperty<Cat>(options),
                                JsonMetadataServices.CreatePropertyInfo<bool>(options, new()
                                {
                                    IsProperty = true, IsPublic = true, DeclaringType = typeof(Cat), PropertyName = "Indoor",
                                    PropertyTypeInfo = Info<bool>(options),
                                    Getter = static o => ((Cat)o).Indoor, Setter = static (o, v) => ((Cat)o).Indoor = v,
                                }),
                            },
                        });
                    return null;
                }

                private static JsonPropertyInfo NameProperty<T>(JsonSerializerOptions options) where T : Animal =>
                    JsonMetadataServices.CreatePropertyInfo<string>(options, new()
                    {
                        IsProperty = true, IsPublic = true, DeclaringType = typeof(Animal), PropertyName = "Name",
                        PropertyTypeInfo = Info<string>(options),
                        Getter = static o => ((Animal)o).Name, Setter = static (o, v) => ((Animal)o).Name = v!,
                    });
            }

            private static JsonTypeInfo<T> Info<T>(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

            private static readonly JsonSerializerOptions s_options = new() { TypeInfoResolver = new Resolver() };

            public static int Run(int input) => input switch
            {
                0 => ObjectValues(),
                1 => ObjectDictionary(),
                2 => UnregisteredRuntimeType(),
                3 => DerivedTypes(),
                4 => TypeBasedSerialization(),
                5 => WeaklyTypedCollections(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int ObjectValues()
            {
                var text = JsonSerializer.Serialize(new List<object> { 1, "b", false }, Info<List<object>>(s_options));
                if (text != "[1,\"b\",false]") return 1;
                var single = JsonSerializer.Serialize<object>("solo", Info<object>(s_options));
                if (single != "\"solo\"") return 2;
                var plain = JsonSerializer.Serialize(new object(), Info<object>(s_options));
                if (plain != "{}") return 3;
                return 100;
            }

            private static int ObjectDictionary()
            {
                var text = JsonSerializer.Serialize(new Dictionary<string, object> { ["n"] = 7, ["s"] = "x", ["f"] = true },
                    Info<Dictionary<string, object>>(s_options));
                if (text != "{\"n\":7,\"s\":\"x\",\"f\":true}") return 1;
                var back = JsonSerializer.Deserialize("{\"a\":[1,2]}", Info<Dictionary<string, object>>(s_options))!;
                if (back["a"] is not JsonElement element || element.GetArrayLength() != 2) return 2;
                return 100;
            }

            private static int UnregisteredRuntimeType()
            {
                try
                {
                    JsonSerializer.Serialize(new List<object> { 1.5 }, Info<List<object>>(s_options));
                    return 1;
                }
                catch (NotSupportedException)
                {
                }
                return 100;
            }

            private static int DerivedTypes()
            {
                var animals = new List<Animal> { new Dog { Name = "Rex", Barks = 3 }, new Cat { Name = "Tom", Indoor = true } };
                var text = JsonSerializer.Serialize(animals, Info<List<Animal>>(s_options));
                if (text != "[{\"$type\":\"dog\",\"Barks\":3,\"Name\":\"Rex\"},{\"$type\":\"cat\",\"Indoor\":true,\"Name\":\"Tom\"}]" &&
                    text != "[{\"$type\":\"dog\",\"Name\":\"Rex\",\"Barks\":3},{\"$type\":\"cat\",\"Name\":\"Tom\",\"Indoor\":true}]")
                    return 1;
                var back = JsonSerializer.Deserialize(text, Info<List<Animal>>(s_options))!;
                if (back.Count != 2 || back[0] is not Dog { Name: "Rex", Barks: 3 } || back[1] is not Cat { Name: "Tom", Indoor: true }) return 2;
                try
                {
                    JsonSerializer.Deserialize("[{\"$type\":\"bird\",\"Name\":\"x\"}]", Info<List<Animal>>(s_options));
                    return 3;
                }
                catch (JsonException)
                {
                }
                return 100;
            }

            private static int TypeBasedSerialization()
            {
                Animal dog = new Dog { Name = "Ace", Barks = 1 };
                var asBase = JsonSerializer.Serialize(dog, typeof(Animal), s_options);
                if (!asBase.StartsWith("{\"$type\":\"dog\"", StringComparison.Ordinal)) return 1;
                var asDerived = JsonSerializer.Serialize(dog, typeof(Dog), s_options);
                if (asDerived.Contains("$type", StringComparison.Ordinal)) return 2;
                try
                {
                    JsonSerializer.Serialize(dog, typeof(Cat), s_options);
                    return 3;
                }
                catch (ArgumentException)
                {
                }
                return 100;
            }
            private static int WeaklyTypedCollections()
            {
                var obj = Info<object>(s_options);
                var str = Info<string>(s_options);
                var list = JsonMetadataServices.CreateIListInfo<IList>(s_options, new() { ElementInfo = obj });
                var dictionary = JsonMetadataServices.CreateIDictionaryInfo<IDictionary>(s_options, new() { KeyInfo = str, ElementInfo = obj });
                var queue = JsonMetadataServices.CreateQueueInfo<Queue>(s_options, new() { ObjectCreator = static () => new Queue(), ElementInfo = obj },
                    static (q, v) => q.Enqueue(v));
                var stack = JsonMetadataServices.CreateStackInfo<Stack>(s_options, new() { ObjectCreator = static () => new Stack(), ElementInfo = obj },
                    static (st, v) => st.Push(v));
                var q = new Queue();
                q.Enqueue(1);
                q.Enqueue("two");
                var st = new Stack();
                st.Push(true);
                st.Push(3);
                var text = JsonSerializer.Serialize<IList>(new ArrayList { 1, "b", false }, list) + "|" +
                    JsonSerializer.Serialize<IDictionary>(new Hashtable { ["k"] = 1 }, dictionary) + "|" +
                    JsonSerializer.Serialize(q, queue) + "|" + JsonSerializer.Serialize(st, stack);
                return text == "[1,\"b\",false]|{\"k\":1}|[1,\"two\"]|[3,true]" ? 100 : 1;
            }
        }
        """;
}
