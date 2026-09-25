using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class CSharp15CompilationTests
{
    [Theory]
    [MemberData(nameof(CSharpFifteenRuntimeConfigurations))]
    public void CompilerPreservesFocusedCSharpFifteenRuntimeSemantics(
        string assemblyName,
        string entryPoint,
        string source,
        int expected,
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            assemblyName,
            source,
            PinnedSdk11(),
            "15.0",
            optimize);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            entryPoint,
            "Run",
            [],
            Target: target));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(InvalidCSharpFifteenSources))]
    public void PinnedRoslynRejectsInvalidCSharpFifteenSource(
        string caseName,
        string source,
        string languageVersion,
        string[] features,
        string expectedDiagnosticCode,
        bool allowUnsafe)
    {
        using var assets = TestAssets.Create();

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithCompiler(
                caseName,
                source,
                PinnedSdk11(),
                languageVersion,
                features,
                allowUnsafe));

        Assert.Contains(expectedDiagnosticCode, exception.DiagnosticCodes);
    }

    [Theory]
    [MemberData(nameof(PinnedDesktopCSharpFifteenNegatives))]
    public void PinnedDesktopRoslynEstablishesCSharpFifteenNegativeOracle(
        string caseName,
        string source,
        string expectedDiagnosticCode)
    {
        using var assets = TestAssets.Create();

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithDesktopFramework(
                caseName,
                source,
                PinnedSdk11(),
                PinnedFramework11(),
                "15.0"));

        Assert.Contains(expectedDiagnosticCode, exception.DiagnosticCodes);
    }

    [Fact]
    public void PinnedDesktopRoslynAcceptsClosedInterfaceCastAtCSharpFifteenPin()
    {
        using var assets = TestAssets.Create();

        var assembly = assets.CompileSourceWithDesktopFramework(
            "ClosedInterfaceRestrictionDesktopOracle",
            """
            public interface IMarker;
            public closed class State;
            public sealed class On : State;
            public sealed class Off : State;
            public static class Oracle
            {
                public static IMarker Convert(State value) => (IMarker)value;
            }
            """,
            PinnedSdk11(),
            PinnedFramework11(),
            "15.0");

        Assert.True(File.Exists(assembly));
    }

    [Fact]
    public void CompilerLowersPinnedCSharpFifteenComposite()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "CSharpFifteenComposite",
            """
            using System.Collections.Generic;

            namespace CSharpFifteenComposite;

            public sealed class Box
            {
                public int[] Values { get; } = new int[2];
            }

            public static class BoxExtensions
            {
                extension(Box box)
                {
                    public int this[int index]
                    {
                        get => box.Values[index];
                        set => box.Values[index] = value;
                    }
                }
            }

            public closed class State;
            public sealed class On : State;
            public sealed class Off : State;

            public sealed record Cat(string Name);
            public sealed record Dog(string Name);
            public union Pet(Cat, Dog);

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    List<int> values = [with(capacity: 4), input, 1];
                    var box = new Box();
                    box[1] = values[0] + values[1];
                    var state = (State)new On() switch { On => 1, Off => 0 };
                    Pet pet = new Dog("Ada");
                    var union = pet switch { Cat cat => cat.Name.Length, Dog dog => dog.Name.Length };
                    return box[1] + state + union + Labeled() - 9;
                }

                private static int Labeled()
                {
                    var result = 0;
                outer:
                    for (var x = 0; x < 3; x++)
                    {
                        for (var y = 0; y < 3; y++)
                        {
                            if (x == 0 && y == 1) continue outer;
                            if (x == 2 && y == 1) break outer;
                            result++;
                        }
                    }
                    return result;
                }
            }
            """,
            PinnedSdk11(),
            "15.0");

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CSharpFifteenComposite.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersPinnedUpdatedMemorySafetyRules()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "CSharpFifteenMemorySafety",
            """
            namespace CSharpFifteenMemorySafety;

            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    var value = input;
                    var pointer = &value;
                    return unsafe(Read(pointer)) + 1;
                }

                private static unsafe int Read(int* value) => unsafe(*value);
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CSharpFifteenMemorySafety.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPreservesSafePointerDeclarationsUnderUpdatedMemoryRules()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "CSharpFifteenSafePointerDeclaration",
            """
            namespace CSharpFifteenSafePointerDeclaration;

            public static class EntryPoint
            {
                private static int* Identity(int* value) => value;

                public static int Run(int input)
                {
                    var value = input;
                    var pointer = unsafe(&value);
                    return unsafe(*Identity(pointer)) + 1;
                }
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CSharpFifteenSafePointerDeclaration.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CSharpFourteenCanConsumeButCannotExtendClosedHierarchy()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "ClosedHierarchyLibrary",
            """
            namespace ClosedHierarchyLibrary;
            public closed class State;
            public sealed class On : State;
            """,
            PinnedSdk11(),
            "15.0");
        var consumer = assets.CompileSourceWithCompiler(
            "ClosedHierarchyConsumer",
            """
            namespace ClosedHierarchyConsumer;
            public static class EntryPoint
            {
                public static int Run(int input) =>
                    new ClosedHierarchyLibrary.On() is ClosedHierarchyLibrary.State
                        ? input + 1
                        : 0;
            }
            """,
            PinnedSdk10(),
            "14.0",
            references: [library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, library],
            "ClosedHierarchyConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithCompiler(
                "ClosedHierarchyInvalidConsumer",
                """
                public sealed class ExternalState : ClosedHierarchyLibrary.State;
                """,
                PinnedSdk10(),
                "14.0",
                references: [library]));
        Assert.Contains("CS9041", exception.DiagnosticCodes);
    }

    [Fact]
    public void CSharpFourteenRejectsPublicUnionSurface()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "UnionLibrary",
            """
            public sealed class Cat;
            public sealed class Dog;
            public union Pet(Cat, Dog);
            public static class UnionApi
            {
                public static Pet Make() => new Dog();
            }
            """,
            PinnedSdk11(),
            "15.0");

        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithCompiler(
                "UnionInvalidConsumer",
                """
                public static class Consumer
                {
                    public static int Run() => UnionApi.Make() is object ? 42 : 0;
                }
                """,
                PinnedSdk10(),
                "14.0",
                references: [library]));

        Assert.Contains("CS8652", exception.DiagnosticCodes);
    }

    [Fact]
    public void CompilerRunsCollectionArgumentsAcrossAssemblyBoundary()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "CollectionArgumentLibrary",
            """
            namespace CollectionArgumentLibrary;
            public sealed class Bag : System.Collections.Generic.IEnumerable<int>
            {
                private readonly System.Collections.Generic.List<int> _values;
                public Bag(int capacity) => _values = new(capacity);
                public int Count => _values.Count;
                public int Capacity => _values.Capacity;
                public int this[int index] => _values[index];
                public void Add(int value) => _values.Add(value);
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() =>
                    _values.GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                    GetEnumerator();
            }
            """,
            PinnedSdk11(),
            "15.0");
        var consumer = assets.CompileSourceWithCompiler(
            "CollectionArgumentConsumer",
            """
            namespace CollectionArgumentConsumer;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    CollectionArgumentLibrary.Bag values = [with(4), input, 1];
                    return values.Count == 2 && values.Capacity >= 4
                        ? values[0] + values[1]
                        : 0;
                }
            }
            """,
            PinnedSdk11(),
            "15.0",
            references: [library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, library],
            "CollectionArgumentConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRunsLabeledControlInConsumerOfOrdinaryLibrary()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "LabeledControlLibrary",
            """
            namespace LabeledControlLibrary;
            public static class Api
            {
                public static int Read(int value) => value;
            }
            """,
            PinnedSdk10(),
            "14.0");
        var consumer = assets.CompileSourceWithCompiler(
            "LabeledControlConsumer",
            """
            namespace LabeledControlConsumer;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = LabeledControlLibrary.Api.Read(input);
                outer:
                    for (var x = 0; x < 2; x++)
                    {
                        for (var y = 0; y < 2; y++)
                        {
                            if (x == 0 && y == 0) continue outer;
                            break outer;
                        }
                    }
                    return result + 1;
                }
            }
            """,
            PinnedSdk11(),
            "15.0",
            references: [library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, library],
            "LabeledControlConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRunsUnionAcrossAssemblyBoundary()
    {
        using var assets = TestAssets.Create();
        var cases = assets.CompileSourceWithCompiler(
            "UnionBoundaryCases",
            """
            namespace UnionBoundaryCases;
            public sealed record Cat(int Value);
            public sealed record Dog(int Value);
            """,
            PinnedSdk11(),
            "15.0");
        var library = assets.CompileSourceWithCompiler(
            "UnionBoundaryLibrary",
            """
            namespace UnionBoundaryLibrary;
            using UnionBoundaryCases;
            public union Pet(Cat, Dog);
            public static class Api
            {
                public static Pet Create(int value) => new Dog(value);
            }
            """,
            PinnedSdk11(),
            "15.0",
            references: [cases]);
        var consumer = assets.CompileSourceWithCompiler(
            "UnionBoundaryConsumer",
            """
            namespace UnionBoundaryConsumer;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var pet = UnionBoundaryLibrary.Api.Create(input + 1);
                    return pet switch
                    {
                        UnionBoundaryCases.Cat cat => cat.Value,
                        UnionBoundaryCases.Dog dog => dog.Value,
                    };
                }
            }
            """,
            PinnedSdk11(),
            "15.0",
            references: [cases, library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, cases, library],
            "UnionBoundaryConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRunsUpdatedAndOriginalMemorySafetyAssembliesTogether()
    {
        using var assets = TestAssets.Create();
        var originalLibrary = assets.CompileSourceWithCompiler(
            "OriginalMemoryLibrary",
            """
            namespace OriginalMemoryLibrary;
            public static class Api
            {
                public static unsafe int Read(int* value) => *value;
            }
            """,
            PinnedSdk10(),
            "14.0",
            allowUnsafe: true);
        var updatedConsumer = assets.CompileSourceWithCompiler(
            "UpdatedMemoryConsumer",
            """
            namespace UpdatedMemoryConsumer;
            public static class EntryPoint
            {
                public static unsafe int Run(int input)
                {
                    var value = input;
                    return unsafe(OriginalMemoryLibrary.Api.Read(&value)) + 1;
                }
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true,
            references: [originalLibrary]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            updatedConsumer,
            [assets.CoreLib, originalLibrary],
            "UpdatedMemoryConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CSharpFourteenRunsSafeApiFromUpdatedMemorySafetyAssembly()
    {
        using var assets = TestAssets.Create();
        var updatedLibrary = assets.CompileSourceWithCompiler(
            "UpdatedMemoryLibrary",
            """
            namespace UpdatedMemoryLibrary;
            public static class Api
            {
                public static int Increment(int value) => value + 1;
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true);
        var originalConsumer = assets.CompileSourceWithCompiler(
            "OriginalMemoryConsumer",
            """
            namespace OriginalMemoryConsumer;
            public static class EntryPoint
            {
                public static int Run(int input) => UpdatedMemoryLibrary.Api.Increment(input);
            }
            """,
            PinnedSdk10(),
            "14.0",
            references: [updatedLibrary]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            originalConsumer,
            [assets.CoreLib, updatedLibrary],
            "OriginalMemoryConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRunsExtensionIndexerAcrossAssemblyBoundary()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "ExtensionIndexerLibrary",
            """
            namespace ExtensionIndexerLibrary;
            public sealed class Buffer
            {
                public int[] Values { get; } = new int[1];
            }
            public static class BufferExtensions
            {
                extension(Buffer buffer)
                {
                    public int this[int index]
                    {
                        get => buffer.Values[index];
                        set => buffer.Values[index] = value;
                    }
                }
            }
            """,
            PinnedSdk11(),
            "15.0");
        var consumer = assets.CompileSourceWithCompiler(
            "ExtensionIndexerConsumer",
            """
            using ExtensionIndexerLibrary;
            namespace ExtensionIndexerConsumer;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var buffer = new Buffer();
                    buffer[0] = input + 1;
                    return buffer[0];
                }
            }
            """,
            PinnedSdk11(),
            "15.0",
            references: [library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, library],
            "ExtensionIndexerConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRunsUpdatedMemorySafetyAcrossAssemblyBoundary()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSourceWithCompiler(
            "UpdatedMemorySafetyLibrary",
            """
            namespace UpdatedMemorySafetyLibrary;
            public static class Api
            {
                public static unsafe int Increment(int value) => value + 1;
                public static unsafe T Identity<T>(T value) => value;
                public static unsafe int Invoke(System.Func<int, int> callback, int value) =>
                    callback(value);
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true);
        var consumer = assets.CompileSourceWithCompiler(
            "UpdatedMemorySafetyConsumer",
            """
            namespace UpdatedMemorySafetyConsumer;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = unsafe(UpdatedMemorySafetyLibrary.Api.Identity(input));
                    return unsafe(UpdatedMemorySafetyLibrary.Api.Invoke(
                        UpdatedMemorySafetyLibrary.Api.Increment,
                        first));
                }
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true,
            references: [library]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            consumer,
            [assets.CoreLib, library],
            "UpdatedMemorySafetyConsumer.EntryPoint",
            "Run",
            []));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));

        using var stream = File.OpenRead(library);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var api = metadata.GetTypeDefinition(FindType(metadata, "Api"));
        var increment = api.GetMethods().Single(handle =>
            metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Increment");
        Assert.Contains(
            "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute",
            AttributeNames(metadata, metadata.GetMethodDefinition(increment).GetCustomAttributes()));
    }

    [Theory]
    [InlineData("NonExhaustiveUnion", """
        public sealed class Cat;
        public sealed class Dog;
        public union Pet(Cat, Dog);
        public static class Invalid
        {
            public static int Read(Pet pet) => pet switch { Cat => 1 };
        }
        """)]
    [InlineData("NonExhaustiveClosedHierarchy", """
        public closed class State;
        public sealed class On : State;
        public sealed class Off : State;
        public static class Invalid
        {
            public static int Read(State state) => state switch { On => 1 };
        }
        """)]
    [InlineData("EmptyClosedHierarchyNonExhaustive", """
        public closed class State;
        public static class Invalid
        {
            public static int Read(State state) => state switch { };
        }
        """)]
    public void PinnedRoslynReportsNonExhaustiveCSharpFifteenPatterns(
        string assemblyName,
        string source)
    {
        using var assets = TestAssets.Create();
        var exception = Assert.Throws<RoslynCompilationException>(() =>
            assets.CompileSourceWithCompiler(
                assemblyName,
                source,
                PinnedSdk11(),
                "15.0",
                warningsAsErrors: true));

        Assert.Contains("CS8509", exception.DiagnosticCodes);
    }

    [Fact]
    public void PinnedRoslynEmitsRequiredCSharpFifteenMetadataShapes()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "CSharpFifteenMetadata",
            """
            public sealed class Cat;
            public sealed class Dog;
            public union Pet(Cat, Dog);
            public closed class State
            {
                public required string Name { get; init; }
            }
            public sealed class On : State;
            public static class MemoryApi
            {
                public static unsafe int Read(int* value) => unsafe(*value);
            }
            """,
            PinnedSdk11(),
            "preview",
            ["updated-memory-safety-rules"],
            allowUnsafe: true);

        using var stream = File.OpenRead(assembly);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var pet = FindType(metadata, "Pet");
        var state = FindType(metadata, "State");

        Assert.Contains(
            "System.Runtime.CompilerServices.UnionAttribute",
            AttributeNames(metadata, metadata.GetTypeDefinition(pet).GetCustomAttributes()));
        Assert.Contains(
            "System.Runtime.CompilerServices.IsClosedTypeAttribute",
            AttributeNames(metadata, metadata.GetTypeDefinition(state).GetCustomAttributes()));
        Assert.Contains(
            metadata.GetTypeDefinition(pet).GetInterfaceImplementations()
                .Select(handle => TypeName(
                    metadata,
                    metadata.GetInterfaceImplementation(handle).Interface)),
            name => name == "System.Runtime.CompilerServices.IUnion");
        Assert.Contains(
            "System.Runtime.CompilerServices.MemorySafetyRulesAttribute",
            AttributeNames(metadata, metadata.GetModuleDefinition().GetCustomAttributes()));
        Assert.DoesNotContain(
            "System.Runtime.CompilerServices.MemorySafetyRulesAttribute",
            AttributeNames(metadata, metadata.GetAssemblyDefinition().GetCustomAttributes()));

        var memorySafetyAttribute = metadata.GetModuleDefinition().GetCustomAttributes()
            .Single(handle => AttributeName(metadata, handle) ==
                "System.Runtime.CompilerServices.MemorySafetyRulesAttribute");
        var valueReader = metadata.GetBlobReader(metadata.GetCustomAttribute(memorySafetyAttribute).Value);
        Assert.Equal(1, valueReader.ReadUInt16());
        Assert.Equal(2, valueReader.ReadInt32());
        Assert.Equal(0, valueReader.ReadUInt16());
        Assert.Equal(0, valueReader.RemainingBytes);

        var constructor = metadata.GetTypeDefinition(state).GetMethods()
            .Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == ".ctor");
        var requiredFeatures = metadata.GetMethodDefinition(constructor).GetCustomAttributes()
            .Where(handle => AttributeName(metadata, handle) ==
                "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute")
            .Select(handle =>
            {
                var reader = metadata.GetBlobReader(metadata.GetCustomAttribute(handle).Value);
                Assert.Equal(1, reader.ReadUInt16());
                return reader.ReadSerializedString();
            })
            .ToArray();
        Assert.Contains("ClosedClasses", requiredFeatures);
        Assert.Contains("RequiredMembers", requiredFeatures);
    }

    private static string PinnedSdk11()
        => PinnedSdk("sdk11Version");

    private static string PinnedSdk10()
        => PinnedSdk("sdk10BuildVersion");

    private static string PinnedFramework11()
        => PinnedSdk("browserRuntimeFrameworkVersion");

    private static string PinnedSdk(string propertyName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "eng",
                "csharp15-toolchain.json")));
        return document.RootElement.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"Pinned SDK version is missing: {propertyName}.");
    }

    public static TheoryData<string, string, string, string[], string, bool> InvalidCSharpFifteenSources => new()
    {
        {
            "CollectionArgumentAfterElement",
            """
            using System.Collections.Generic;
            public static class Invalid
            {
                public static List<int> Run() => [1, with(capacity: 4)];
            }
            """,
            "15.0",
            [],
            "CS9354",
            false
        },
        {
            "LabeledJumpEscapesFinally",
            """
            public static class Invalid
            {
                public static void Run()
                {
                target:
                    while (true)
                    {
                        try { }
                        finally { break target; }
                    }
                }
            }
            """,
            "15.0",
            [],
            "CS0157",
            false
        },
        {
            "AmbiguousExtensionIndexer",
            """
            public sealed class Box;
            public static class First
            {
                extension(Box box) { public int this[int index] => 1; }
            }
            public static class Second
            {
                extension(Box box) { public int this[int index] => 2; }
            }
            public static class Invalid
            {
                public static int Run(Box box) => box[0];
            }
            """,
            "15.0",
            [],
            "CS0021",
            false
        },
        {
            "UpdatedMemoryRulesRequirePreview",
            """
            public static class Invalid
            {
                public static unsafe int Run(int* value) => unsafe(*value);
            }
            """,
            "15.0",
            ["updated-memory-safety-rules"],
            "CS8652",
            true
        },
        {
            "ArrayCollectionArgument",
            """
            public static class Invalid
            {
                public static int[] Run() => [with(), 1];
            }
            """,
            "15.0",
            [],
            "CS9355",
            false
        },
        {
            "SpanCollectionArgument",
            """
            public static class Invalid
            {
                public static System.Span<int> Run() => [with(), 1];
            }
            """,
            "15.0",
            [],
            "CS9355",
            false
        },
        {
            "AmbiguousCollectionConstructor",
            """
            using System.Collections.Generic;
            public static class Invalid
            {
                public static List<int> Run() => [with(default)];
            }
            """,
            "15.0",
            [],
            "CS0121",
            false
        },
        {
            "InapplicableCollectionConstructor",
            """
            using System.Collections.Generic;
            public static class Invalid
            {
                public static List<int> Run() => [with("bad"), 1];
            }
            """,
            "15.0",
            [],
            "CS1503",
            false
        },
        {
            "UnknownLabeledBreak",
            """
            public static class Invalid
            {
                public static void Run()
                {
                    while (true) break missing;
                }
            }
            """,
            "15.0",
            [],
            "CS9393",
            false
        },
        {
            "NonEnclosingLabeledBreak",
            """
            public static class Invalid
            {
                public static void Run()
                {
                target:
                    while (false) { }
                    while (true) break target;
                }
            }
            """,
            "15.0",
            [],
            "CS9393",
            false
        },
        {
            "DuplicateOverlappingLoopLabel",
            """
            public static class Invalid
            {
                public static void Run()
                {
                outer:
                    while (true)
                    {
                    outer:
                        while (true) break outer;
                    }
                }
            }
            """,
            "15.0",
            [],
            "CS0158",
            false
        },
        {
            "ContinueToSwitchLabel",
            """
            public static class Invalid
            {
                public static void Run(int value)
                {
                target:
                    switch (value)
                    {
                        default: continue target;
                    }
                }
            }
            """,
            "15.0",
            [],
            "CS9394",
            false
        },
        {
            "DuplicateUnionCase",
            """
            public union Number(int, int);
            """,
            "15.0",
            [],
            "CS0111",
            false
        },
        {
            "InvalidUnionCase",
            """
            public union Invalid(void, int);
            """,
            "15.0",
            [],
            "CS1536",
            false
        },
        {
            "ForbiddenUnionBaseConversion",
            """
            public abstract record Animal;
            public sealed record Cat : Animal;
            public sealed record Dog : Animal;
            public union Pet(Cat, Dog);
            public static class Invalid
            {
                public static Pet Run(Animal value) => value;
            }
            """,
            "15.0",
            [],
            "CS0029",
            false
        },
        {
            "ForbiddenUnionInterfaceConversion",
            """
            public interface IAnimal;
            public sealed record Cat : IAnimal;
            public sealed record Dog : IAnimal;
            public union Pet(Cat, Dog);
            public static class Invalid
            {
                public static Pet Run(IAnimal value) => value;
            }
            """,
            "15.0",
            [],
            "CS0029",
            false
        },
        {
            "RequiresUnsafeCallOutsideContext",
            """
            public static class Invalid
            {
                public static unsafe int Dangerous(int value) => value;
                public static int Run(int value) => Dangerous(value);
            }
            """,
            "preview",
            ["updated-memory-safety-rules"],
            "CS9362",
            true
        },
        {
            "PointerOperationRequiresUnsafeExpression",
            """
            public static class Invalid
            {
                public static int Read(int* value) => *value;
            }
            """,
            "preview",
            ["updated-memory-safety-rules"],
            "CS9360",
            true
        },
        {
            "UnsupportedInterfaceCollectionArguments",
            """
            public static class Invalid
            {
                public static System.Collections.Generic.IEnumerable<int> Run() =>
                    [with("bad"), 1];
            }
            """,
            "15.0",
            [],
            "CS9357",
            false
        },
        {
            "InaccessibleCollectionConstructor",
            """
            public sealed class Bag : System.Collections.Generic.IEnumerable<int>
            {
                private Bag(int capacity) { }
                public void Add(int value) { }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                    GetEnumerator();
            }
            public static class Invalid
            {
                public static Bag Run() => [with(1), 2];
            }
            """,
            "15.0",
            [],
            "CS0122",
            false
        },
        {
            "UnnamedExtensionIndexerReceiver",
            """
            public sealed class Box;
            public static class Extensions
            {
                extension(Box)
                {
                    public int this[int index] => 0;
                }
            }
            """,
            "15.0",
            [],
            "CS9303",
            false
        },
        {
            "InvalidClosedGenericSubtype",
            """
            public closed class State<T>;
            public class InvalidSubtype<T> : State<int>;
            """,
            "15.0",
            [],
            "CS9383",
            false
        },
    };

    public static TheoryData<string, string, string> PinnedDesktopCSharpFifteenNegatives => new()
    {
        {
            "DynamicCollectionArgumentDesktopOracle",
            """
            using System.Collections.Generic;
            public static class Invalid
            {
                public static List<int> Run(dynamic capacity) => [with(capacity), 1];
            }
            """,
            "CS9356"
        },
        {
            "ExtensionIndexerExpressionTreeDesktopOracle",
            """
            public sealed class Box;
            public static class Extensions
            {
                extension(Box box) { public int this[int index] => 42; }
            }
            public static class Invalid
            {
                public static System.Linq.Expressions.Expression<System.Func<Box, int>> Run() =>
                    box => box[0];
            }
            """,
            "CS9296"
        },
        {
            "ISetCollectionArgumentsUnavailableAtPinDesktopOracle",
            """
            public static class Invalid
            {
                public static System.Collections.Generic.ISet<int> Run() =>
                    [with(comparer: System.Collections.Generic.EqualityComparer<int>.Default), 1];
            }
            """,
            "CS9174"
        },
    };

    public static IEnumerable<object[]> CSharpFifteenRuntimeConfigurations()
    {
        foreach (var row in CSharpFifteenRuntimeCases)
        {
            foreach (var optimize in new[] { false, true })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    yield return [.. row, optimize, target];
                }
            }
        }
    }

    public static TheoryData<string, string, string, int> CSharpFifteenRuntimeCases => new()
    {
        {
            "CollectionArgumentEvaluation",
            "CollectionArgumentEvaluation.EntryPoint",
            """
            using System.Collections.Generic;
            namespace CollectionArgumentEvaluation;
            public static class EntryPoint
            {
                private static int _trace;
                private static int Mark(int marker, int value)
                {
                    _trace = _trace * 10 + marker;
                    return value;
                }
                private static int[] Spread()
                {
                    _trace = _trace * 10 + 3;
                    return [22];
                }
                public static int Run(int _)
                {
                    _trace = 0;
                    List<int> values = [with(capacity: Mark(1, 4)), Mark(2, 20), .. Spread()];
                    return values.Count == 2 && values[0] + values[1] == 42 ? _trace : -1;
                }
            }
            """,
            231
        },
        {
            "ExtensionIndexerEvaluation",
            "ExtensionIndexerEvaluation.EntryPoint",
            """
            namespace ExtensionIndexerEvaluation;
            public sealed class Box<T>
            {
                public T[] Values { get; }
                public Box(T value) => Values = [value];
            }
            public static class Extensions
            {
                extension<T>(Box<T> box)
                {
                    public T this[int index]
                    {
                        get => box.Values[index];
                        set => box.Values[index] = value;
                    }
                }
            }
            public static class EntryPoint
            {
                private static int _receiverCount;
                private static int _indexCount;
                private static readonly Box<int> Box = new(0);
                private static Box<int> Receiver()
                {
                    _receiverCount++;
                    return Box;
                }
                private static int Index()
                {
                    _indexCount++;
                    return 0;
                }
                public static int Run(int _)
                {
                    _receiverCount = 0;
                    _indexCount = 0;
                    Receiver()[Index()] = 42;
                    var value = Receiver()[Index()];
                    return value == 42 && _receiverCount == 2 && _indexCount == 2 ? 42 : 0;
                }
            }
            """,
            42
        },
        {
            "CollectionBuilderArguments",
            "CollectionBuilderArguments.EntryPoint",
            """
            namespace CollectionBuilderArguments;
            [System.Runtime.CompilerServices.CollectionBuilder(
                typeof(BagBuilder),
                nameof(BagBuilder.Create))]
            public sealed class Bag<T>(T[] values)
            {
                public T[] Values { get; } = values;
                public System.Collections.Generic.IEnumerator<T> GetEnumerator() =>
                    ((System.Collections.Generic.IEnumerable<T>)Values).GetEnumerator();
            }
            public static class BagBuilder
            {
                public static Bag<T> Create<T>(
                    int bonus,
                    System.ReadOnlySpan<T> values)
                {
                    var result = new T[values.Length];
                    values.CopyTo(result);
                    Bonus = bonus;
                    return new Bag<T>(result);
                }
                public static int Bonus;
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    Bag<int> values = [with(1), 20, 21];
                    return values.Values[0] + values.Values[1] + BagBuilder.Bonus;
                }
            }
            """,
            42
        },
        {
            "CollectionArgumentExceptionOrder",
            "CollectionArgumentExceptionOrder.EntryPoint",
            """
            using System.Collections.Generic;
            namespace CollectionArgumentExceptionOrder;
            public static class EntryPoint
            {
                private static int _trace;
                private static int MarkArgument()
                {
                    _trace = _trace * 10 + 1;
                    return 4;
                }
                private static int ThrowElement()
                {
                    _trace = _trace * 10 + 2;
                    throw new System.InvalidOperationException();
                }
                public static int Run(int _)
                {
                    _trace = 0;
                    try
                    {
                        List<int> values = [with(capacity: MarkArgument()), ThrowElement()];
                        return values.Count;
                    }
                    catch (System.InvalidOperationException)
                    {
                        return _trace;
                    }
                }
            }
            """,
            12
        },
        {
            "CollectionArgumentThrowingWithArgument",
            "CollectionArgumentThrowingWithArgument.EntryPoint",
            """
            using System.Collections.Generic;
            namespace CollectionArgumentThrowingWithArgument;
            public static class EntryPoint
            {
                private static int _trace;
                private static int ThrowArgument()
                {
                    _trace = _trace * 10 + 1;
                    throw new System.InvalidOperationException();
                }
                private static int MarkElement()
                {
                    _trace = _trace * 10 + 2;
                    return 42;
                }
                public static int Run(int _)
                {
                    _trace = 0;
                    try
                    {
                        List<int> values = [with(capacity: ThrowArgument()), MarkElement()];
                        return values.Count;
                    }
                    catch (System.InvalidOperationException)
                    {
                        return _trace == 1 ? 42 : 0;
                    }
                }
            }
            """,
            42
        },
        {
            "LabeledFinally",
            "LabeledFinally.EntryPoint",
            """
            namespace LabeledFinally;
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var trace = 0;
                outer:
                    for (var x = 0; x < 3; x++)
                    {
                        try
                        {
                            if (x == 0) continue outer;
                            if (x == 1) break outer;
                        }
                        finally
                        {
                            trace = trace * 10 + x + 1;
                        }
                    }
                    return trace == 12 ? 42 : 0;
                }
            }
            """,
            42
        },
        {
            "RefExtensionIndexer",
            "RefExtensionIndexer.EntryPoint",
            """
            namespace RefExtensionIndexer;
            public sealed class Buffer
            {
                public int[] Values { get; } = new int[1];
            }
            public static class Extensions
            {
                extension(Buffer buffer)
                {
                    public ref int this[int index] => ref buffer.Values[index];
                }
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var buffer = new Buffer();
                    ref var slot = ref buffer[0];
                    slot = 42;
                    return buffer.Values[0];
                }
            }
            """,
            42
        },
        {
            "LabeledEnumerationDisposal",
            "LabeledEnumerationDisposal.EntryPoint",
            """
            namespace LabeledEnumerationDisposal;
            public sealed class Sequence
            {
                public Enumerator GetEnumerator() => new();
            }
            public sealed class Enumerator : System.IDisposable
            {
                private int _value;
                public int Current => _value;
                public bool MoveNext() => ++_value <= 2;
                public void Dispose() => EntryPoint.Disposed++;
            }
            public static class EntryPoint
            {
                public static int Disposed;
                public static int Run(int _)
                {
                    Disposed = 0;
                outer:
                    for (var pass = 0; pass < 2; pass++)
                    {
                        foreach (var value in new Sequence())
                        {
                            if (value == 1) break outer;
                        }
                    }
                    return Disposed == 1 ? 42 : 0;
                }
            }
            """,
            42
        },
        {
            "UnionClosedStorage",
            "UnionClosedStorage.EntryPoint",
            """
            namespace UnionClosedStorage;
            public sealed record Cat(int Value);
            public sealed record Dog(int Value);
            public union Pet(Cat, Dog);
            public sealed class Holder<T>(T value)
            {
                public T Value = value;
            }
            public closed class State<T>;
            public sealed class On<T> : State<T>;
            public sealed class Off<T> : State<T>;
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    Pet empty = default!;
                    if (empty is not null) return 0;
                    Pet[] values = [new Cat(20), new Dog(22)];
                    var holder = new Holder<Pet>(values[1]);
                    for (var index = 0; index < 128; index++)
                    {
                        var garbage = new byte[1024];
                        if (garbage.Length == 0) return 0;
                    }
                    System.GC.Collect();
                    object boxed = holder.Value;
                    var sum = values[0] switch { Cat cat => cat.Value, Dog dog => dog.Value }
                        + ((Pet)boxed switch { Cat cat => cat.Value, Dog dog => dog.Value });
                    State<int> state = new On<int>();
                    return sum == 42 && state is On<int> ? 42 : 0;
                }
            }
            """,
            42
        },
        {
            "ClosedRecordExhaustiveness",
            "ClosedRecordExhaustiveness.EntryPoint",
            """
            namespace ClosedRecordExhaustiveness;
            public closed record Shape;
            public sealed record Circle(int Radius) : Shape;
            public sealed record Square(int Side) : Shape;
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    Shape shape = new Circle(42);
                    return shape switch
                    {
                        Circle circle => circle.Radius,
                        Square square => square.Side,
                    };
                }
            }
            """,
            42
        },
        {
            "ValueUnionStorage",
            "ValueUnionStorage.EntryPoint",
            """
            namespace ValueUnionStorage;
            public union Number(int, long);
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    Number[] values = [20, 22L];
                    object boxed = values[1];
                    var left = values[0] switch { int value => value, long value => (int)value };
                    var right = ((Number)boxed) switch { int value => value, long value => (int)value };
                    return left + right;
                }
            }
            """,
            42
        },
        {
            "UnionPatternForms",
            "UnionPatternForms.EntryPoint",
            """
            namespace UnionPatternForms;
            public sealed record Cat(int Value);
            public sealed record Dog(int Value);
            public union Pet(Cat, Dog);
            public static class EntryPoint
            {
                private static int Read(Pet? pet)
                {
                    if (pet is Cat { Value: > 10 and < 30 } cat && pet is Cat or Dog)
                    {
                        return cat.Value;
                    }
                    return pet switch
                    {
                        Dog(var value) => value,
                        null => 0,
                        _ => 0,
                    };
                }
                public static int Run(int _)
                {
                    Pet? left = new Cat(20);
                    Pet? right = new Dog(22);
                    return Read(left) + Read(right);
                }
            }
            """,
            42
        },
        {
            "GenericUnionDeclaration",
            "GenericUnionDeclaration.EntryPoint",
            """
            namespace GenericUnionDeclaration;
            public sealed record None;
            public sealed record Some<T>(T Value);
            public union Option<T>(None, Some<T>);
            public static class EntryPoint
            {
                private static int Read(Option<int> option) => option switch
                {
                    None => 0,
                    Some<int>(var value) => value,
                };
                public static int Run(int _)
                {
                    Option<int> left = new Some<int>(20);
                    Option<int> right = new Some<int>(22);
                    return Read(left) + Read(right);
                }
            }
            """,
            42
        },
        {
            "TypeParameterUnionCase",
            "TypeParameterUnionCase.EntryPoint",
            """
            namespace TypeParameterUnionCase;
            public sealed record Error(int Code);
            public union Result<T>(T, Error);
            public static class EntryPoint
            {
                private static int Read(Result<int> result) => result switch
                {
                    int value => value,
                    Error error => error.Code,
                };
                public static int Run(int _)
                {
                    Result<int> left = 20;
                    Result<int> right = new Error(22);
                    return Read(left) + Read(right);
                }
            }
            """,
            42
        },
        {
            "EmptyClosedHierarchy",
            "EmptyClosedHierarchy.EntryPoint",
            """
            namespace EmptyClosedHierarchy;
            public closed class Empty;
            public static class EntryPoint
            {
                public static int Run(int _) => 42;
            }
            """,
            42
        },
        {
            "UnionInterfaceNullableAndNestedCases",
            "UnionInterfaceNullableAndNestedCases.EntryPoint",
            """
            namespace UnionInterfaceNullableAndNestedCases;
            public interface ICase { }
            public sealed record Marker(int Value) : ICase;
            public union Inner(Marker, string);
            public union Mixed(ICase, int?, Inner);
            public static class EntryPoint
            {
                private static int Read(Mixed value) => value switch
                {
                    Marker marker => marker.Value,
                    int number => number,
                    Inner inner => inner switch
                    {
                        Marker marker => marker.Value,
                        string text => text.Length,
                    },
                    _ => 0,
                };
                public static int Run(int _)
                {
                    Mixed first = (ICase)new Marker(20);
                    Mixed second = (int?)22;
                    Inner inner = "";
                    Mixed nested = inner;
                    return Read(first) + Read(second) + Read(nested);
                }
            }
            """,
            42
        },
        {
            "CollectionConstructorVariants",
            "CollectionConstructorVariants.EntryPoint",
            """
            using System.Collections.Generic;
            namespace CollectionConstructorVariants;
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    List<int> empty = [with()];
                    HashSet<int> set = [with(comparer: EqualityComparer<int>.Default), 20, 22];
                    return empty.Count == 0 && set.Count == 2 && set.Contains(20) && set.Contains(22)
                        ? 42
                        : 0;
                }
            }
            """,
            42
        },
        {
            "LabeledSwitchAndSameFinally",
            "LabeledSwitchAndSameFinally.EntryPoint",
            """
            namespace LabeledSwitchAndSameFinally;
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var trace = 0;
                outer:
                    for (var index = 0; index < 3; index++)
                    {
                        switch (index)
                        {
                            case 0: trace += 20; continue outer;
                            case 1: trace += 22; break outer;
                        }
                    }
                    try { }
                    finally
                    {
                    local:
                        for (var index = 0; index < 2; index++)
                        {
                            if (index == 0) continue local;
                            break local;
                        }
                    }
                    return trace;
                }
            }
            """,
            42
        },
        {
            "ExtensionIndexerResolution",
            "ExtensionIndexerResolution.EntryPoint",
            """
            namespace ExtensionIndexerResolution;
            public sealed class Box<T>(T value)
            {
                public T Value = value;
            }
            public static class Extensions
            {
                extension<T>(Box<T> box)
                {
                    public T this[int index] => box.Value;
                }
                extension(Box<string> box)
                {
                    public int this[string index] => box.Value.Length + index.Length;
                }
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var box = new Box<string>("forty");
                    return box[0].Length + box["two"] + 29;
                }
            }
            """,
            42
        },
        {
            "RefReceiverExtensionIndexer",
            "RefReceiverExtensionIndexer.EntryPoint",
            """
            namespace RefReceiverExtensionIndexer;
            public struct Buffer
            {
                public int Value;
            }
            public static class Extensions
            {
                extension(ref Buffer buffer)
                {
                    public ref int this[int index] => ref buffer.Value;
                }
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var buffer = new Buffer();
                    ref var value = ref buffer[0];
                    value = 42;
                    return buffer.Value;
                }
            }
            """,
            42
        },
        {
            "ExtensionIndexerNullAndListPattern",
            "ExtensionIndexerNullAndListPattern.EntryPoint",
            """
            namespace ExtensionIndexerNullAndListPattern;
            public sealed class Buffer(int value)
            {
                public int Length => 1;
                public int Value = value;
            }
            public static class Extensions
            {
                extension(Buffer buffer)
                {
                    public int this[int index] => buffer.Value;
                }
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    var buffer = new Buffer(42);
                    if (buffer is not [42] || buffer[^1] != 42) return 0;
                    Buffer? missing = null;
                    try
                    {
                        return missing[0];
                    }
                    catch (System.NullReferenceException)
                    {
                        return 42;
                    }
                }
            }
            """,
            42
        },
        {
            "ClosedHierarchyInteractions",
            "ClosedHierarchyInteractions.EntryPoint",
            """
            namespace ClosedHierarchyInteractions;
            public interface IState { }
            public closed class State<T> : IState;
            public sealed class On<T> : State<T>;
            public sealed class Off<T> : State<T>;
            public static class EntryPoint
            {
                private static int Match<T>(T value) where T : State<int> => value switch
                {
                    On<int> => 42,
                    Off<int> => 0,
                };
                public static int Run(int _)
                {
                    IState state = new On<int>();
                    return state is State<int> typed ? Match(typed) : 0;
                }
            }
            """,
            42
        },
        {
            "NestedClosedSubtype",
            "NestedClosedSubtype.EntryPoint",
            """
            namespace NestedClosedSubtype;
            public closed class State;
            public static class Cases
            {
                public sealed class On : State;
                public sealed class Off : State;
            }
            public static class EntryPoint
            {
                public static int Run(int _)
                {
                    State state = new Cases.On();
                    return state switch
                    {
                        Cases.On => 42,
                        Cases.Off => 0,
                    };
                }
            }
            """,
            42
        },
        {
            "ClosedHierarchyInterfaceCast",
            "ClosedHierarchyInterfaceCast.EntryPoint",
            """
            namespace ClosedHierarchyInterfaceCast;
            public interface IMarker;
            public closed class State;
            public sealed class On : State;
            public sealed class Off : State;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    State state = new On();
                    try
                    {
                        _ = (IMarker)state;
                        return 0;
                    }
                    catch (System.InvalidCastException)
                    {
                        return 42;
                    }
                }
            }
            """,
            42
        },
    };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static TypeDefinitionHandle FindType(MetadataReader metadata, string name) =>
        metadata.TypeDefinitions.Single(handle =>
            metadata.GetString(metadata.GetTypeDefinition(handle).Name) == name);

    private static IEnumerable<string> AttributeNames(
        MetadataReader metadata,
        CustomAttributeHandleCollection attributes) =>
        attributes.Select(handle =>
        {
            var constructor = metadata.GetCustomAttribute(handle).Constructor;
            return constructor.Kind switch
            {
                HandleKind.MemberReference => TypeName(
                    metadata,
                    metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent),
                HandleKind.MethodDefinition => TypeName(
                    metadata,
                    metadata.GetMethodDefinition((MethodDefinitionHandle)constructor)
                        .GetDeclaringType()),
                _ => string.Empty,
            };
        });

    private static string AttributeName(MetadataReader metadata, CustomAttributeHandle handle)
    {
        var constructor = metadata.GetCustomAttribute(handle).Constructor;
        return constructor.Kind switch
        {
            HandleKind.MemberReference => TypeName(
                metadata,
                metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent),
            HandleKind.MethodDefinition => TypeName(
                metadata,
                metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
            _ => string.Empty,
        };
    }

    private static string TypeName(MetadataReader metadata, EntityHandle handle)
    {
        StringHandle namespaceHandle;
        StringHandle nameHandle;
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var reference = metadata.GetTypeReference((TypeReferenceHandle)handle);
                namespaceHandle = reference.Namespace;
                nameHandle = reference.Name;
                break;
            case HandleKind.TypeDefinition:
                var definition = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                namespaceHandle = definition.Namespace;
                nameHandle = definition.Name;
                break;
            default:
                return string.Empty;
        }
        var @namespace = metadata.GetString(namespaceHandle);
        var name = metadata.GetString(nameHandle);
        return string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
    }
}
