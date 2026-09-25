using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConstructedGenericCastCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerCastsBaseReferencesToExactConstructedGenericTypes(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ConstructedGenericCast.Library",
            LibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "ConstructedGenericCast.Application",
            ApplicationSource,
            "ConstructedGenericCast.Application.EntryPoint",
            optimize,
            target,
            41,
            ImmutableArray.Create(library)));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerCastsCovariantDelegatesAcrossAssemblyAndGenericBoundaries(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ConstructedGenericCast.Library",
            DelegateVarianceLibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "ConstructedGenericCast.Application",
            DelegateVarianceApplicationSource,
            "EntryPoint",
            optimize,
            target,
            41,
            ImmutableArray.Create(library)));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerResolvesConstructedGenericMetadataThroughAnExternalInterface(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ConstructedGenericResolver.Library",
            ResolverLibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "ConstructedGenericResolver.Application",
            ResolverApplicationSource,
            "ConstructedGenericResolver.Application.EntryPoint",
            optimize,
            target,
            41,
            ImmutableArray.Create(library)));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerDispatchesAnInterfaceInheritedFromAConstructedGenericBase(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "InheritedGenericInterface.Library",
            InheritedInterfaceLibrarySource);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "InheritedGenericInterface.Application",
            InheritedInterfaceApplicationSource,
            "InheritedGenericInterface.Application.EntryPoint",
            optimize,
            target,
            41,
            ImmutableArray.Create(library)));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerLaysOutNonGenericClassesThroughConstructedGenericBaseChains(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"ConstructedGenericBaseChain_{optimize}_{target}",
            ConstructedGenericBaseChainSource,
            "ConstructedGenericBaseChain.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, result);
    }

    private const string LibrarySource = """
        namespace ConstructedGenericCast.Library;

        public abstract class Metadata;

        public sealed class Metadata<T>(T value) : Metadata
        {
            public T Value { get; } = value;
        }

        public static class Cache
        {
            public static Metadata Get<T>(T value) => new Metadata<T>(value);
        }
        """;

    private const string ApplicationSource = """
        using ConstructedGenericCast.Library;

        namespace ConstructedGenericCast.Application;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public static class EntryPoint
        {
            public static int Run(int input) =>
                ((Metadata<Payload>)Cache.Get(new Payload(input + 1))).Value.Value;
        }
        """;

    private const string ResolverLibrarySource = """
        #nullable enable
        using System;

        namespace ConstructedGenericResolver.Library;

        public abstract class Metadata;

        public sealed class Metadata<T>(T value) : Metadata
        {
            public T Value { get; } = value;
        }

        public interface IResolver
        {
            Metadata? Resolve(Type type, Options options);
        }

        public sealed class Options(IResolver resolver)
        {
            public Metadata Get(Type type) =>
                resolver.Resolve(type, this) ?? throw new InvalidOperationException();
        }
        """;

    private const string ResolverApplicationSource = """
        #nullable enable
        using System;
        using ConstructedGenericResolver.Library;

        namespace ConstructedGenericResolver.Application;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public sealed class GeneratedResolver : IResolver
        {
            public Metadata? Resolve(Type type, Options options) =>
                type == typeof(Payload)
                    ? new Metadata<Payload>(new Payload(42))
                    : null;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var options = new Options(new GeneratedResolver());
                return ((Metadata<Payload>)options.Get(typeof(Payload))).Value.Value;
            }
        }
        """;

    private const string InheritedInterfaceLibrarySource = """
        using System;
        using System.Collections;
        using System.Collections.Generic;

        namespace InheritedGenericInterface.Library;

        public sealed class Item;

        public sealed class OtherItem;

        public abstract class Configuration<T> : IList<T>
        {
            private readonly List<T> _items = new();

            public int Count => _items.Count + 42;

            public bool IsReadOnly => false;

            public T this[int index]
            {
                get => _items[index];
                set => _items[index] = value;
            }

            public void Add(T item) => _items.Add(item);

            public void Clear() => _items.Clear();

            public bool Contains(T item) => _items.Contains(item);

            public void CopyTo(T[] array, int arrayIndex) =>
                _items.CopyTo(array, arrayIndex);

            public IEnumerator<T> GetEnumerator() =>
                _items.GetEnumerator();

            public int IndexOf(T item) => _items.IndexOf(item);

            public void Insert(int index, T item) => _items.Insert(index, item);

            public bool Remove(T item) => _items.Remove(item);

            public void RemoveAt(int index) => _items.RemoveAt(index);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public sealed class Options
        {
            private sealed class ConcreteConfiguration : Configuration<Item>;
            private sealed class OtherConfiguration : Configuration<OtherItem>;

            public IList<Item> Create() => new ConcreteConfiguration();

            public IList<OtherItem> CreateOther() => new OtherConfiguration();
        }

        public static class Factory
        {
            public static IList<Item> Create() =>
                new Options().Create();

            public static IList<OtherItem> CreateOther() =>
                new Options().CreateOther();
        }
        """;

    private const string InheritedInterfaceApplicationSource = """
        using InheritedGenericInterface.Library;

        namespace InheritedGenericInterface.Application;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                _ = Factory.CreateOther().Count;
                return Factory.Create().Count;
            }
        }
        """;

    private const string ConstructedGenericBaseChainSource = """
        namespace ConstructedGenericBaseChain;

        public abstract class GenericBase<T>
        {
            private readonly T _value;

            protected GenericBase(T value) => _value = value;

            protected T Value => _value;
        }

        public abstract class GenericMiddle<T, TSelf>(T value) : GenericBase<T>(value)
            where TSelf : GenericMiddle<T, TSelf>;

        public abstract class ClosedMiddle(int value) : GenericMiddle<int, ClosedMiddle>(value);

        public sealed class Concrete(int value) : ClosedMiddle(value)
        {
            public int Read() => Value;
        }

        public static class EntryPoint
        {
            public static int Run(int input) => new Concrete(input + 1).Read();
        }
        """;
    private const string DelegateVarianceLibrarySource = """
        using System;

        namespace DelegateVarianceContracts
        {
            public interface IProvider
            {
            }

            public class Product
            {
            }

            public sealed class SpecificProduct : Product
            {
            }

            public static class GenericReferenceCast
            {
                public static T Cast<T>(object value) => (T)value;

                public static bool Is<T>(object value) => value is T;
            }
        }
        """;

    private const string DelegateVarianceApplicationSource = """
        using System;
        using DelegateVarianceContracts;

        public static class EntryPoint
        {
            public static int Run(int value)
            {
            Func<IProvider, SpecificProduct> specific = static _ => new SpecificProduct();
            object boxed = specific;
            Func<IProvider, Product>? tested = boxed as Func<IProvider, Product>;

            if (tested is null)
            {
                return -1;
            }

            if (boxed is not Func<IProvider, Product>)
                {
                    return -1;
                }

                Func<IProvider, Product> direct = (Func<IProvider, Product>)boxed;
                Func<IProvider, Product> generic =
                    GenericReferenceCast.Cast<Func<IProvider, Product>>(boxed);

                if (!GenericReferenceCast.Is<Func<IProvider, Product>>(boxed) ||
                    direct is null ||
                    generic is null)
                {
                    return -2;
                }

                return value + 1;
            }
        }
        """;

}
