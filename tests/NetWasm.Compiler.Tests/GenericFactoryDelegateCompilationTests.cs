using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericFactoryDelegateCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerKeepsCapturingFactoriesDistinctAcrossGenericCallers(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            namespace GenericFactoryDelegateFixture;

            public abstract class Product(int value)
            {
                public int Value { get; } = value;
            }

            public abstract class GenericProduct<T>(int value) : Product(value);

            public sealed class FirstProduct<T>(int value) : GenericProduct<T>(value);

            public sealed class SecondProduct<T>(int value) : GenericProduct<T>(value);

            public static class ProductFactory
            {
                private static readonly System.Collections.Generic.Dictionary<
                    string,
                    System.Collections.Generic.List<Product>> Cache = new();

                private static Product Create<T>(
                    System.Type productType,
                    string name,
                    System.Func<Product> factory)
                {
                    if (!Cache.TryGetValue(name, out var products))
                    {
                        products = new System.Collections.Generic.List<Product>();
                        Cache.Add(name, products);
                    }

                    foreach (var product in products)
                    {
                        if (product.GetType() == productType)
                        {
                            return product;
                        }
                    }

                    var created = factory();
                    products.Add(created);
                    return created;
                }

                public static FirstProduct<T> CreateFirst<T>(int value) =>
                    (FirstProduct<T>)Create<T>(
                        typeof(FirstProduct<T>),
                        "first",
                        () => new FirstProduct<T>(value));

                public static SecondProduct<T> CreateSecond<T>(int value) =>
                    (SecondProduct<T>)Create<T>(
                        typeof(SecondProduct<T>),
                        "second",
                        () => new SecondProduct<T>(value));
            }

            """;
        const string applicationSource = """
            namespace GenericFactoryDelegateFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (typeof(FirstProduct<int>) == typeof(SecondProduct<int>))
                    {
                        return 901;
                    }
                    Product directFirst = new FirstProduct<int>(input);
                    Product directSecond = new SecondProduct<int>(input + 1);
                    var products = new System.Collections.Generic.List<Product>
                    {
                        directFirst,
                    };
                    if (products.Contains(directSecond))
                    {
                        return 903;
                    }
                    var first = ProductFactory.CreateFirst<int>(input);
                    if (first.GetType() == typeof(SecondProduct<int>))
                    {
                        return 902;
                    }
                    var second = ProductFactory.CreateSecond<int>(input + 1);
                    return first.Value * 10 + second.Value;
                }
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource(
                "GenericFactoryDelegateLibrary",
                librarySource)
            : assets.CompileSource(
                "GenericFactoryDelegateLibrary",
                librarySource);
        var assembly = optimize
            ? assets.CompileOptimizedSource(
                "GenericFactoryDelegateApplication",
                applicationSource,
                library)
            : assets.CompileSource(
                "GenericFactoryDelegateApplication",
                applicationSource,
                library);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib, library],
            "GenericFactoryDelegateFixture.EntryPoint",
            "Run",
            [],
            target));
        var observed = CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            1,
            target,
            result.StaticDataEnd);

        Assert.Equal(12, observed);
    }
}
