using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A delegate's run-time type is the exact delegate type it was created as, whatever static type holds it.
// Microsoft.Extensions.DependencyInjection reads a factory registration's implementation type from
// ImplementationFactory.GetType().GenericTypeArguments[1], where the factory is a Func<IServiceProvider, TImpl>
// stored as a Func<IServiceProvider, object>. Delegate types are sealed, but a variant one's static type does not
// name its instances' type, so GetType() on it cannot fold to typeof of the static type.
public sealed class DelegateRuntimeTypeCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ADelegateKeepsTheTypeItWasCreatedAs(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DelegateRuntimeTypeFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DelegateRuntimeTypeFixture.EntryPoint",
            "Run", [new("run", "DelegateRuntimeTypeFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace DelegateRuntimeTypeFixture;

        public interface IProvider { }
        public sealed class Provider : IProvider { }
        public interface IService { }
        public sealed class Implementation : IService { }

        public sealed class Descriptor
        {
            public Descriptor(Func<IProvider, object> factory) => Factory = factory;
            public Func<IProvider, object> Factory { get; }
            public static Descriptor Describe<TService, TImplementation>(Func<IProvider, TImplementation> factory)
                where TImplementation : class, TService => new(factory);
        }

        public sealed class Options { }

        public static class Registrations
        {
            // A static lambda in a generic method: the compiler caches it in a generic closure class.
            public static Descriptor ValidateOnStart<TOptions>() where TOptions : class =>
                Descriptor.Describe<IService, Implementation>(static provider => new Implementation());
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var descriptor = Descriptor.Describe<IService, Implementation>(_ => new Implementation());
                // Read through the variant static type, as DI's GetImplementationType does.
                var typed = descriptor.Factory;
                if (typed.GetType() != typeof(Func<IProvider, Implementation>)) return 10;
                if (typed.GetType().GenericTypeArguments[1] != typeof(Implementation)) return 11;
                Action<object> anything = _ => { };
                Action<string> text2 = anything;
                if (text2.GetType() != typeof(Action<object>)) return 12;
                Func<int> number = () => 1;
                if (number.GetType() != typeof(Func<int>)) return 13;
                object factory = descriptor.Factory;
                var arguments = factory.GetType().GenericTypeArguments;
                if (arguments.Length != 2) return 1;
                if (arguments[0] != typeof(IProvider)) return 2;
                if (arguments[1] != typeof(Implementation)) return 3;
                if (factory.GetType() != typeof(Func<IProvider, Implementation>)) return 4;
                if (factory is not Func<IProvider, Implementation>) return 5;
                if (factory is Func<IProvider, string>) return 6;
                object cached = Registrations.ValidateOnStart<Options>().Factory;
                var cachedArguments = cached.GetType().GenericTypeArguments;
                if (cachedArguments.Length != 2 || cachedArguments[1] != typeof(Implementation)) return 8;
                if (cached.GetType() != typeof(Func<IProvider, Implementation>)) return 9;
                Func<string> text = () => "x";
                object boxed = text;
                if (boxed.GetType() != typeof(Func<string>) || boxed is Func<Uri>) return 7;
                return input + 1;
            }
        }
        """;
}
