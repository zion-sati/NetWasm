using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class GenericCompilationTests
{
    [Fact]
    public void CompilerTreatsBoxOfAReferenceGenericSpecializationAsIdentity()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericReferenceBoxFixture",
            """
            namespace GenericReferenceBoxFixture;

            public sealed class ReferenceValue
            {
                public int Value;
            }

            public static class EntryPoint
            {
                private static object Box<T>(T value) => value!;

                public static int Run(int input)
                {
                    var value = new ReferenceValue { Value = input };
                    var boxed = Box(value);
                    return object.ReferenceEquals(value, boxed)
                        ? ((ReferenceValue)boxed).Value
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericReferenceBoxFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerSupportsClosedGenericActionAndFuncDelegates()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericDelegateFixture",
            """
            namespace GenericDelegateFixture;

            public static class EntryPoint
            {
                private static int _observed;

                private static void Unused(
                    System.Action<int, int, int, int, int, int, int, int> callback)
                {
                    callback(1, 2, 3, 4, 5, 6, 7, 8);
                }

                public static int Run(int input)
                {
                    System.Action<int> observe = Observe;
                    System.Func<int, int, int> add = Add;
                    observe(input);
                    return add(_observed, 1);
                }

                private static void Observe(int value) => _observed = value;
                private static int Add(int left, int right) => left + right;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericDelegateFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Contains(result.Program.DelegateTypes,
            type => type.CanonicalName.Contains("System.Action`1", StringComparison.Ordinal));
        Assert.Contains(result.Program.DelegateTypes,
            type => type.CanonicalName.Contains("System.Func`3", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Program.DelegateTypes,
            type => type.CanonicalName.Contains("System.Action`8", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Program.MethodInstances.Values,
            method => method.Definition.Name == "Unused");
    }

    [Fact]
    public void CompilerFoldsClosedReferenceContainmentTraitsBeforeReachability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ReferenceContainmentFixture",
            """
            using System.Runtime.CompilerServices;

            namespace ReferenceContainmentFixture;

            public struct Pair<T>
            {
                public int Number;
                public T Value;
            }

            public struct Nested<T>
            {
                public Pair<T> Pair;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = RuntimeHelpers.IsReferenceOrContainsReferences<int>() ? 1 : 0;
                    result |= RuntimeHelpers.IsReferenceOrContainsReferences<string>() ? 2 : 0;
                    result |= RuntimeHelpers.IsReferenceOrContainsReferences<Pair<int>>() ? 4 : 0;
                    result |= RuntimeHelpers.IsReferenceOrContainsReferences<Nested<string>>() ? 8 : 0;
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ReferenceContainmentFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(10, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
        // The traits fold before reachability, so the inspected types are never constructed.
        Assert.DoesNotContain(result.Program.ConstructedTypes, type =>
            type.CanonicalName.Contains("Pair`1", StringComparison.Ordinal) ||
            type.CanonicalName.Contains("Nested`1", StringComparison.Ordinal));
    }

    [Fact]
    public void CompilerEmitsDeterministicClosedScalarGenericSpecializations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericCompilerFixture",
            """
            namespace GenericCompilerFixture;

            public sealed class Box<T>
            {
                public Box(T value) => Value = value;
                public T Value;
            }

            public static class EntryPoint
            {
                // The boxes escape, so allocation scalar replacement keeps them and
                // their constructors.
                private static object Escaped;

                private static T Identity<T>(T value)
                {
                    T copy = value;
                    return copy;
                }

                public static int Run(int input)
                {
                    var box = new Box<int>(Identity(input) + 1);
                    // A run-time argument keeps constant call folding from removing this specialization.
                    var text = new Box<string>(Identity(input >= 0 ? "abc" : "xyz"));
                    Escaped = box;
                    Escaped = text;
                    return box.Value + text.Value.Length;
                }
            }
            """);
        CompilerOptions options = new(
            assembly,
            [assets.CoreLib],
            "GenericCompilerFixture.EntryPoint",
            "Run",
            []);

        var first = NetWasmCompiler.Compile(options);
        var second = NetWasmCompiler.Compile(options);

        Assert.Equal(first.ApplicationModule, second.ApplicationModule);
        Assert.Equal(4, first.Program.ConstructedMethods.Count);
        var specializations = first.Program.MethodInstances.Values
            .Where(method => method.Definition.Name == "Identity")
            .ToArray();
        Assert.Contains(specializations, method =>
            Assert.Single(method.MethodArguments).CanonicalName == "primitive:i4");
        Assert.Contains(specializations, method =>
            Assert.Single(method.MethodArguments).CanonicalName == "primitive:string");
        Assert.All(first.Program.ConstructedRootMaps, pair =>
            Assert.Equal(0, pair.Value.SlotCount));
        Assert.Equal(2, first.Program.ConstructedTypes.Count);
        var intBox = first.Program.ConstructedTypes.Single(type =>
            Assert.Single(type.TypeArguments).CanonicalName == "primitive:i4");
        var stringBox = first.Program.ConstructedTypes.Single(type =>
            Assert.Single(type.TypeArguments).CanonicalName == "primitive:string");
        var intBoxLayout = first.Layouts.ConstructedObjectLayouts[intBox];
        var stringBoxLayout = first.Layouts.ConstructedObjectLayouts[stringBox];
        Assert.Equal(8, intBoxLayout.Size);
        Assert.True(intBoxLayout.ReferenceOffsets.IsEmpty);
        Assert.Equal(8, stringBoxLayout.Size);
        Assert.True(stringBoxLayout.ReferenceOffsets.AsSpan().SequenceEqual([4]));
        var entryRoots = first.Program.RootMaps[first.Program.EntryPoint!.Key];
        Assert.Contains(entryRoots.Safepoints.Values, safepoint =>
            safepoint.Roots.Any(root => root.Kind == RootSourceKind.Local) &&
            safepoint.Roots.Any(root => root.Kind == RootSourceKind.EvaluationStack));
        Assert.Equal(45, ExecuteWithNode(first.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerUsesCanonicalWidthsForBooleanByteCharAndInt32Fields()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PrimitiveLayoutCompilerFixture",
            """
            namespace PrimitiveLayoutCompilerFixture;

            public sealed class Mixed
            {
                public bool Flag;
                public byte Small;
                public char Code;
                public int Value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new Mixed();
                    value.Flag = true;
                    value.Small = 7;
                    value.Code = 'Ω';
                    value.Value = input;
                    return (value.Flag ? 1 : 0) + value.Small + value.Code + value.Value;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "PrimitiveLayoutCompilerFixture.EntryPoint",
            "Run",
            []));
        using var metadata = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fieldRepository = MetadataCompilationTestActors.Fields(snapshot);
        var fields = result.Program.Types
                .Select(types.GetTypeDefinition)
                .Single(type => type.FullName == "PrimitiveLayoutCompilerFixture.Mixed")
                .Fields
                .Select(field => (
                    Field: fieldRepository.GetField(field),
                    Layout: result.Layouts.InstanceFieldLayouts[field]))
                .ToDictionary(pair => pair.Field.Name, pair => pair.Layout, StringComparer.Ordinal);

        Assert.Equal((4, 1), (fields["Flag"].Offset, fields["Flag"].Size));
        Assert.Equal((5, 1), (fields["Small"].Offset, fields["Small"].Size));
        Assert.Equal((6, 2), (fields["Code"].Offset, fields["Code"].Size));
        Assert.Equal((8, 4), (fields["Value"].Offset, fields["Value"].Size));
        Assert.Equal(986, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesAddressCarriedStructConstructionAndInstanceCalls()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StructCompilerFixture",
            """
            namespace StructCompilerFixture;

            public interface IRead
            {
                int Sum();
            }

            public struct Pair : IRead
            {
                public int Left;
                public int Right;

                public Pair(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Sum() => Left + Right;
            }

            public static class EntryPoint
            {
                private static int Read<T>(T value) where T : IRead => value.Sum();
                private static T Identity<T>(T value) => value;

                public static int Run(int input)
                {
                    Pair original = new(input, input + 1);
                    Pair copy = original;
                    original.Left = 1_000;
                    Pair cleared = default;
                    cleared.Right = 1;
                    object boxed = copy;
                    Pair unboxed = Identity((Pair)boxed);
                    copy.Right = 1_000;
                    return Read(unboxed) + cleared.Sum();
                }
            }
            """);
        CompilerOptions options = new(
            assembly,
            [assets.CoreLib],
            "StructCompilerFixture.EntryPoint",
            "Run",
            []);

        var first = NetWasmCompiler.Compile(options);
        var second = NetWasmCompiler.Compile(options);

        Assert.Equal(first.ApplicationModule, second.ApplicationModule);
        Assert.True(first.ApplicationModule.AsSpan().IndexOf("value_frame_enter"u8) >= 0);
        Assert.True(first.ApplicationModule.AsSpan().IndexOf("value_frame_leave"u8) >= 0);
        Assert.Contains(first.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Read" && method.IsConstructed);
        Assert.Contains(first.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Identity" && method.IsConstructed);
        Assert.Equal(84, ExecuteWithNode(first.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPublishesEmbeddedStructReferencesWhenBoxing()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ReferenceStructCompilerFixture",
            """
            namespace ReferenceStructCompilerFixture;

            public sealed class Node { }

            public struct Pair
            {
                public Node Left;
                public Node Right;
            }

            public static class EntryPoint
            {
                private static int Consume(Node value, int input) => input;

                public static int Run(int input)
                {
                    var node = new Node();
                    Pair value = default;
                    value.Left = node;
                    value.Right = node;
                    object boxed = value;
                    Pair copy = (Pair)boxed;
                    return Consume(copy.Left, input);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ReferenceStructCompilerFixture.EntryPoint",
            "Run",
            []));

        var roots = result.Program.RootMaps[result.Program.EntryPoint!.Key];
        var box = roots.Safepoints.Values.Single(safepoint =>
            safepoint.Roots.Count(root =>
                root.Kind == RootSourceKind.EvaluationStack && root.IsIndirect) == 2);
        Assert.Contains(box.Roots, root =>
            root.Kind == RootSourceKind.EvaluationStack && root.ByteOffset == 0);
        Assert.Contains(box.Roots, root =>
            root.Kind == RootSourceKind.EvaluationStack && root.ByteOffset == 4);
        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerSeparatesClosedGenericStaticStorageAndInitializers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericStaticCompilerFixture",
            """
            namespace GenericStaticCompilerFixture;

            public static class Storage<T>
            {
                public static T Value;
                public static int Marker = 3;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Storage<int>.Value = input;
                    Storage<string>.Value = "abc";
                    Storage<int>.Marker = Storage<int>.Marker + 1;
                    Storage<string>.Marker = Storage<string>.Marker + 1;
                    return Storage<int>.Value + Storage<string>.Value.Length
                        + Storage<int>.Marker + Storage<string>.Marker;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericStaticCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(2, result.Program.ConstructedStaticInitializers.Length);
        var valueFields = result.Program.ConstructedFields.Values
            .Where(field => field.Definition.Name == "Value")
            .OrderBy(field => field.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, valueFields.Length);
        Assert.NotEqual(
            result.Layouts.ConstructedStaticFieldLayouts[valueFields[0].CanonicalName].Address,
            result.Layouts.ConstructedStaticFieldLayouts[valueFields[1].CanonicalName].Address);
        Assert.Equal(54, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            invocationCount: 2));
    }

    [Fact]
    public void CompilerExecutesClosedGenericArraysOfReferenceBearingValues()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericValueArrayCompilerFixture",
            """
            namespace GenericValueArrayCompilerFixture;

            public sealed class Node { }

            public struct Pair
            {
                public Node Reference;
                public int Number;
            }

            public static class EntryPoint
            {
                private static T First<T>(T[] values) => values[0];
                private static int Consume(Node value, int number) => number;

                public static int Run(int input)
                {
                    var node = new Node();
                    var values = new Pair[2];
                    values[0].Reference = node;
                    values[0].Number = input;
                    var other = new Node();
                    Pair copy = First(values);
                    values[0].Number = 1_000;
                    return Consume(copy.Reference, copy.Number) + Consume(other, 0);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericValueArrayCompilerFixture.EntryPoint",
            "Run",
            []));

        var descriptor = Assert.Single(
            result.Layouts.ValueTypeDescriptors,
            descriptor => descriptor.Type.FullName ==
                "GenericValueArrayCompilerFixture.Pair");
        Assert.Equal(8, descriptor.Size);
        var pair = result.Layouts.ValueLayouts[descriptor.Type];
        Assert.True(pair.ReferenceOffsets.AsSpan().SequenceEqual([0]));
        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "First" && method.IsConstructed);
        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerSpecializesGenericDefaultAndNewConstraints()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericCreationCompilerFixture",
            """
            namespace GenericCreationCompilerFixture;

            public struct Pair
            {
                public int Number;
            }

            public sealed class Node
            {
                public int Number;

                public Node()
                {
                    Number = 7;
                }
            }

            public static class EntryPoint
            {
                private static T Create<T>() where T : new() => new T();

                public static int Run(int input)
                {
                    Pair pair = Create<Pair>();
                    Node node = Create<Node>();
                    return input + pair.Number + node.Number;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericCreationCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Create" && method.IsConstructed &&
            method.MethodArguments[0].IsValueType);
        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Create" && method.IsConstructed &&
            !method.MethodArguments[0].IsValueType);
        Assert.Equal(48, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
