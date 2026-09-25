using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GenericMethodDispatchCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesConstructedGenericBaseAutoPropertyAcrossClosedDescendants(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace ConstructedGenericBaseProperty;

            public abstract class Converter
            {
                public abstract Type Type { get; }
            }

            public abstract class Converter<T> : Converter
            {
                public sealed override Type Type { get; } = typeof(T);
            }

            public sealed class ValueConverter<T> : Converter<T>;

            public sealed class NullableConverter<T> : Converter<T?>
                where T : struct;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Converter converter = input switch
                    {
                        1 => new ValueConverter<string>(),
                        2 => new ValueConverter<byte>(),
                        3 => new ValueConverter<short>(),
                        4 => new ValueConverter<int>(),
                        5 => new ValueConverter<long>(),
                        6 => new NullableConverter<byte>(),
                        7 => new NullableConverter<short>(),
                        8 => new NullableConverter<long>(),
                        _ => new NullableConverter<int>(),
                    };

                    return converter.Type == typeof(int?) ? 41 : -1;
                }
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("ConstructedGenericBaseProperty", source)
            : assets.CompileSource("ConstructedGenericBaseProperty", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConstructedGenericBaseProperty.EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(41, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerUnifiesRuntimeTypeIdentityAcrossReferenceAssemblyAliases(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string contract = """
            using System;

            namespace AliasedRuntimeTypes;

            public sealed class Box<T>;

            public static class Factory
            {
                public static Type GetType<T>() => typeof(Box<T>);
            }
            """;
        var reference = assets.CompileSource("AliasedRuntimeTypes.Reference", contract);
        var implementation = assets.CompileSource(
            "AliasedRuntimeTypes.Implementation",
            contract);
        const string applicationSource = """
            using AliasedRuntimeTypes;

            namespace AliasedRuntimeTypesApplication;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    Factory.GetType<int>() == typeof(Box<int>) ? 41 : -1;
            }
            """;
        var application = optimized
            ? assets.CompileOptimizedSource(
                "AliasedRuntimeTypes.Application",
                applicationSource,
                reference)
            : assets.CompileSource(
                "AliasedRuntimeTypes.Application",
                applicationSource,
                reference);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            application,
            [implementation, assets.CoreLib],
            "AliasedRuntimeTypesApplication.EntryPoint",
            "Run",
            [],
            target,
            ReferenceAssemblyAliases:
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add(
                    "AliasedRuntimeTypes.Reference",
                    "AliasedRuntimeTypes.Implementation")));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(41, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerResolvesAliasedGenericOverloadsByClosedParameterSignature(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string contract = """
            namespace AliasedGenericOverloads;

            public sealed class Options;
            public sealed class TypeInfo<T>;
            public sealed class Product<T>(int code)
            {
                public int Code { get; } = code;
            }

            public static class Factory
            {
                public static Product<T> Create<T>(Options options) => new(0);
                public static Product<T> Create<T>(TypeInfo<T> typeInfo) => new(0);
            }
            """;
        var reference = assets.CompileSource(
            "AliasedGenericOverloads.Reference",
            contract);
        var implementation = assets.CompileSource(
            "AliasedGenericOverloads.Implementation",
            """
            namespace AliasedGenericOverloads;

            public sealed class Options;
            public sealed class TypeInfo<T>;
            public sealed class Product<T>(int code)
            {
                public int Code { get; } = code;
            }

            public static class Factory
            {
                public static Product<T> Create<T>(Options options) => new(41);
                public static Product<T> Create<T>(TypeInfo<T> typeInfo) => new(99);
            }
            """);
        const string applicationSource = """
            using AliasedGenericOverloads;

            namespace AliasedGenericOverloadsApplication;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    Factory.Create<int>(new Options()).Code;
            }
            """;
        var application = optimized
            ? assets.CompileOptimizedSource(
                "AliasedGenericOverloads.Application",
                applicationSource,
                reference)
            : assets.CompileSource(
                "AliasedGenericOverloads.Application",
                applicationSource,
                reference);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            application,
            [implementation, assets.CoreLib],
            "AliasedGenericOverloadsApplication.EntryPoint",
            "Run",
            [],
            target,
            ReferenceAssemblyAliases:
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add(
                    "AliasedGenericOverloads.Reference",
                    "AliasedGenericOverloads.Implementation")));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(41, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Fact]
    public void CovariantEnumerationRetainsTheConstructedEnumeratorReturnAbi()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CovariantEnumeratorFixture",
            """
            using System.Collections.Generic;
            namespace CovariantEnumeratorFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IEnumerable<string> strings = new List<string> { "value" };
                    IEnumerable<object> objects = strings;
                    var count = 0;
                    foreach (var value in objects)
                        count += value is null ? 100 : 1;
                    return count;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CovariantEnumeratorFixture.EntryPoint",
            "Run",
            []));
        var currentSite = Assert.Single(result.Program.DispatchCallSites.Values, site =>
            site.Declaration.Definition.Name == "get_Current" &&
            site.Declaration.DeclaringType.CanonicalName.Contains(
                "System.Collections.Generic.IEnumerator`1<primitive:object>",
                StringComparison.Ordinal));
        var currentTarget = Assert.Single(currentSite.Targets, target =>
            target.ReceiverType.CanonicalName.Contains(
                "System.Collections.Generic.List`1+Enumerator<primitive:string>",
                StringComparison.Ordinal));

        Assert.Equal(CliValueKind.ManagedReference, currentTarget.Method.Signature.ReturnType);
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void NestedGenericValueTypeParticipatesInCovariantInterfaceAssignability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NestedVariantAssignabilityFixture",
            """
            namespace NestedVariantAssignabilityFixture;
            public static class EntryPoint { public static int Run(int input) => input; }
            """);
        using var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(snapshot);
        var interfaces = MetadataCompilationTestActors.ImplementedInterfaces(snapshot);
        var baseTypes = new BaseTypeResolverFactory().Create(
            typeFinder,
            identities,
            baseTypeIdentities);
        var relationships = new TypeRelationshipClassifierFactory().Create(
            typeFinder,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes);
        var enumeratorDefinition = identities.GetTypeIdentity(typeFinder.FindType(
            "System.Collections.Generic.List`1+Enumerator").Key);
        var interfaceDefinition = identities.GetTypeIdentity(typeFinder.FindType(
            "System.Collections.Generic.IEnumerator`1").Key);
        var stringType = identities.GetTypeIdentity(typeFinder.FindType("System.String").Key);
        var objectType = identities.GetTypeIdentity(typeFinder.FindType("System.Object").Key);

        var enumerator = CliTypeIdentity.GenericInstantiation(
            enumeratorDefinition,
            [stringType]);
        var target = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [objectType]);

        Assert.True(relationships.Classify(enumerator, target).IsHierarchyAssignable);
    }

    [Fact]
    public void GenericVarianceParticipatesInClosedInterfaceAssignability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "VariantAssignabilityFixture",
            """
            namespace VariantAssignabilityFixture;
            public class Base { }
            public sealed class Derived : Base { }
            public interface IInput<in T> { }
            public interface IOutput<out T> { }
            public sealed class Both : IInput<object>, IOutput<Derived> { }
            public sealed class ValueOutput : IOutput<int> { }
            public static class EntryPoint { public static int Run(int input) => input; }
            """);
        using var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = metadata.Snapshot;
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(snapshot);
        var interfaces = MetadataCompilationTestActors.ImplementedInterfaces(snapshot);
        var baseTypes = new BaseTypeResolverFactory().Create(
            typeFinder,
            identities,
            baseTypeIdentities);
        var relationships = new TypeRelationshipClassifierFactory().Create(
            typeFinder,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes);
        var both = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.Both").Key);
        var derived = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.Derived").Key);
        var baseType = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.Base").Key);
        var objectType = identities.GetTypeIdentity(typeFinder.FindType(
            "System.Object").Key);
        var valueOutput = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.ValueOutput").Key);
        var input = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.IInput`1").Key);
        var output = identities.GetTypeIdentity(typeFinder.FindType(
            "VariantAssignabilityFixture.IOutput`1").Key);

        Assert.True(relationships.Classify(
            both,
            CliTypeIdentity.GenericInstantiation(input, [derived])).IsHierarchyAssignable);
        Assert.True(relationships.Classify(
            both,
            CliTypeIdentity.GenericInstantiation(output, [baseType])).IsHierarchyAssignable);
        Assert.False(relationships.Classify(
            valueOutput,
            CliTypeIdentity.GenericInstantiation(output, [objectType])).IsHierarchyAssignable);

    }

    [Fact]
    public void VariantInterfaceCastAndDispatchUseTheCompatibleExplicitSlot()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "VariantCastDispatchFixture",
            """
            namespace VariantCastDispatchFixture;
            public class Base { }
            public sealed class Derived : Base { }
            public interface IInput<in T> { int Read(T value); }
            public sealed class Both : IInput<object>, IInput<Base>
            {
                int IInput<object>.Read(object value) => 41;
                int IInput<Base>.Read(Base value) => 42;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object value = new Both();
                    return ((IInput<Derived>)value).Read(new Derived());
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "VariantCastDispatchFixture.EntryPoint",
            "Run",
            []));

        var site = Assert.Single(result.Program.TypeTestSites.Values);
        Assert.Equal(
            "VariantCastDispatchFixture.Both",
            Assert.Single(site.MatchingTypes).FullName);
        var dispatch = Assert.Single(result.Program.DispatchCallSites.Values);
        var target = Assert.Single(dispatch.Targets);
        Assert.Equal("VariantCastDispatchFixture.Both", target.ReceiverType.FullName);
        Assert.Equal(41, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            0));
    }

    [Fact]
    public void ExplicitVariantInterfacesSelectTheExactConstructedDeclaration()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "VariantExplicitDispatchFixture",
            """
            namespace VariantExplicitDispatchFixture;

            public interface IProducer<out T> { T Produce(); }
            public class Base { }
            public sealed class Derived : Base { }

            public sealed class Both : IProducer<Base>, IProducer<Derived>
            {
                Base IProducer<Base>.Produce() => new Base();
                Derived IProducer<Derived>.Produce() => new Derived();
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IProducer<Base> producer = new Both();
                    return producer.Produce() is Derived ? 0 : input;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "VariantExplicitDispatchFixture.EntryPoint",
            "Run",
            []));

        using (var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]))
        {
            var typeFinder = MetadataCompilationTestActors.TypeFinder(metadata.Snapshot);
            Assert.Equal(
                CliGenericVariance.Covariant,
                Assert.Single(typeFinder.FindType(
                    "VariantExplicitDispatchFixture.IProducer`1")
                    .GenericParameterVariances));

        }

        Assert.Equal(41, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            41));
    }

    [Fact]
    public void CompilerExecutesGenericInterfaceMethodsAcrossClosedArguments()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericInterfaceMethodFixture",
            """
            namespace GenericInterfaceMethodFixture;

            public interface IMapper
            {
                T Map<T>(T value);
            }

            public sealed class Mapper : IMapper
            {
                public T Map<T>(T value) => value;
            }

            public sealed class ExplicitMapper : IMapper
            {
                T IMapper.Map<T>(T value) => value;
            }

            public static class EntryPoint
            {
                private static int Read(IMapper mapper, int input) =>
                    mapper.Map<int>(input) + mapper.Map<string>("abc").Length;

                public static int Run(int input) =>
                    Read(new Mapper(), input) + Read(new ExplicitMapper(), 1);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericInterfaceMethodFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name.Contains("Map", StringComparison.Ordinal) &&
            method.MethodArguments.Any(argument => argument.CanonicalName == "primitive:i4"));
        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name.Contains("Map", StringComparison.Ordinal) &&
            method.MethodArguments.Any(argument => argument.CanonicalName == "primitive:string"));
        Assert.Equal(48, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesGenericVirtualOverridesAndBaseCalls()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericVirtualMethodFixture",
            """
            namespace GenericVirtualMethodFixture;

            public class Base
            {
                public virtual T Echo<T>(T value) => value;
            }

            public sealed class Derived : Base
            {
                public override T Echo<T>(T value) => value;
                public T BaseEcho<T>(T value) => base.Echo(value);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Base value = new Derived();
                    var derived = (Derived)value;
                    return value.Echo(input) + value.Echo("abc").Length
                        + derived.BaseEcho(1);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericVirtualMethodFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(45, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRejectsReachableOpenGenericEntryPointDeterministically()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "OpenGenericEntryFixture",
            """
            namespace OpenGenericEntryFixture;

            public static class EntryPoint
            {
                public static int Run<T>(int input) => input;
            }
            """);
        var options = new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "OpenGenericEntryFixture.EntryPoint",
            "Run",
            []);

        var first = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        var second = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));

        Assert.Equal(first.Message, second.Message);
        Assert.Contains("generic", first.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompilerTrimsUnreachableGenericAndValueTaskFamilies()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GenericTrimmingFixture",
            """
            using System.Threading.Tasks;

            namespace GenericTrimmingFixture;

            public sealed class Box<T>
            {
                public T Value = default!;
            }

            public static class EntryPoint
            {
                private static T Used<T>(T value) => value;
                private static T Unused<T>(T value) => value;
                private static ValueTask<int> UnusedValueTask() => new(99);

                public static int Run(int input) => Used(input);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GenericTrimmingFixture.EntryPoint",
            "Run",
            []));

        Assert.DoesNotContain(result.Program.MethodInstances.Values, method =>
            method.Definition.Name is "Unused" or "UnusedValueTask");
        Assert.DoesNotContain(result.Program.ConstructedTypes, type =>
            type.CanonicalName.Contains("ValueTask", StringComparison.Ordinal) ||
            type.CanonicalName.Contains("Box`1", StringComparison.Ordinal));
        Assert.True(result.ApplicationModule.AsSpan().IndexOf("ValueTask"u8) < 0);
        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerRecursivelyDiscoversNestedClosedSpecializations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RecursiveGenericFixture",
            """
            namespace RecursiveGenericFixture;

            public sealed class Box<T>
            {
                public Box(T value) => Value = value;
                public T Value;
            }

            public static class EntryPoint
            {
                private static Box<T> Outer<T>(T value) => Inner<T>(value);
                private static Box<T> Inner<T>(T value) => new(value);

                public static int Run(int input) =>
                    Outer(input).Value + Outer("abc").Value.Length;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RecursiveGenericFixture.EntryPoint",
            "Run",
            []));

        var innerSpecializations = result.Program.MethodInstances.Values
            .Where(method => method.Definition.Name == "Inner")
            .ToArray();
        Assert.Equal(2, innerSpecializations.Length);
        Assert.Equal(44, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerDispatchesConstrainedCallsClosedOverInterfaceAndUnsealedTypes(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace ConstrainedReferenceDispatchFixture;

            public interface IValue
            {
                int Read();
            }

            public class BaseValue : IValue
            {
                public virtual int Read() => 2;
            }

            public sealed class DerivedValue : BaseValue
            {
                public override int Read() => 7;
            }

            public sealed class InterfaceValue : IValue
            {
                public int Read() => 4;
            }

            public readonly struct StructValue : IValue
            {
                public int Read() => 3;
            }

            public enum Marker
            {
                Alpha = 1,
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IValue interfaceValue = input == 0
                        ? new InterfaceValue()
                        : new DerivedValue();
                    BaseValue baseValue = new DerivedValue();
                    var dispatchResult = Invoke<IValue>(interfaceValue) * 1000 +
                        Invoke<BaseValue>(baseValue) * 100 +
                        Invoke<InterfaceValue>(new InterfaceValue()) * 10 +
                        Invoke<StructValue>(new StructValue());
                    IFormattable formatter = Marker.Alpha;
                    return dispatchResult * 10 + Format<IFormattable>(formatter);
                }

                private static int Invoke<T>(T value)
                    where T : IValue => value.Read();

                private static int Format<T>(T value)
                    where T : IFormattable => value.ToString(null, null).Length;
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource(
                "ConstrainedReferenceDispatchFixture",
                source)
            : assets.CompileSource(
                "ConstrainedReferenceDispatchFixture",
                source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConstrainedReferenceDispatchFixture.EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(47435, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
        Assert.Equal(77435, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConstrainedGenericToStringResolvesClosedReferenceAndValueTargets(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace ConstrainedToStringFixture;

            public static class EntryPoint
            {
                private static string Text<T>(T value) => value!.ToString()!;

                public static int Run(int input) => input switch
                {
                    0 => Text("hello").Length,
                    1 => Text(10).Length,
                    _ => 0,
                };
            }
            """;
        var assembly = optimized
            ? assets.CompileOptimizedSource("ConstrainedToStringFixture", source)
            : assets.CompileSource("ConstrainedToStringFixture", source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConstrainedToStringFixture.EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(5, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
        Assert.Equal(2, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd));
    }

    [Fact]
    public void CompilerSpecializesStaticAbstractInterfaceCalls()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StaticAbstractInterfaceFixture",
            """
            namespace StaticAbstractInterfaceFixture;

            public interface ITransform<TSelf> where TSelf : ITransform<TSelf>
            {
                static abstract int Apply(int value);
            }

            public readonly struct Increment : ITransform<Increment>
            {
                public static int Apply(int value) => value + 1;
            }

            public readonly struct Double : ITransform<Double>
            {
                public static int Apply(int value) => value * 2;
            }

            public static class EntryPoint
            {
                private static int Invoke<T>(int value) where T : ITransform<T> =>
                    T.Apply(value);

                public static int Run(int input) =>
                    Invoke<Increment>(input) + Invoke<Double>(input);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StaticAbstractInterfaceFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(124, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerClosesStaticAbstractGenericMathContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace StaticAbstractGenericMathFixture;

            public interface IConstants<TSelf> where TSelf : IConstants<TSelf>
            {
                static abstract TSelf One { get; }
                static abstract int ToInt(TSelf value);
            }

            public interface IArithmetic<TSelf> : IConstants<TSelf>
                where TSelf : IArithmetic<TSelf>
            {
                static abstract TSelf Add(TSelf left, TSelf right);
            }

            public interface ICheckedArithmetic<TSelf> : IConstants<TSelf>
                where TSelf : ICheckedArithmetic<TSelf>
            {
                static abstract TSelf operator +(TSelf left, TSelf right);
                static abstract TSelf operator checked +(TSelf left, TSelf right);
            }

            public readonly struct ExplicitNumber(int value) : IArithmetic<ExplicitNumber>
            {
                private readonly int _value = value;

                static ExplicitNumber IConstants<ExplicitNumber>.One => new(1);
                static int IConstants<ExplicitNumber>.ToInt(ExplicitNumber value) => value._value;
                static ExplicitNumber IArithmetic<ExplicitNumber>.Add(
                    ExplicitNumber left,
                    ExplicitNumber right) => new(left._value + right._value);
            }

            public readonly struct CheckedNumber(int value) : ICheckedArithmetic<CheckedNumber>
            {
                private readonly int _value = value;

                public static CheckedNumber One => new(1);
                public static int ToInt(CheckedNumber value) => value._value;
                public static CheckedNumber operator +(
                    CheckedNumber left,
                    CheckedNumber right) => new(left._value + right._value);
                public static CheckedNumber operator checked +(
                    CheckedNumber left,
                    CheckedNumber right) => new(checked(left._value + right._value));
            }

            public static class EntryPoint
            {
                private static int AddOne<T>(T value) where T : IArithmetic<T> =>
                    T.ToInt(T.Add(value, T.One));

                private static int CheckedAddOne<T>(T value) where T : ICheckedArithmetic<T> =>
                    T.ToInt(checked(value + T.One));

                private static int Recurse<T>(T value) where T : IArithmetic<T> => AddOne(value);

                public static int Run(int input) =>
                    Recurse(new ExplicitNumber(input)) + CheckedAddOne(new CheckedNumber(input));
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "StaticAbstractGenericMathFixture",
            source,
            "StaticAbstractGenericMathFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(84, result);
    }
}
