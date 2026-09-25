using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DelegateBindingConversionCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesReferenceDelegateBindingFamilies(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            using System;

            namespace DelegateBindingLibrary;

            public class Base;

            public sealed class Derived : Base;

            public class Receiver
            {
                public bool Accept(object value) => value is Derived;

                public virtual bool VirtualAccept(object value) => false;
            }

            public sealed class DerivedReceiver : Receiver
            {
                public override bool VirtualAccept(object value) => value is Derived;
            }

            public static class Methods
            {
                public static bool SameBase(Base left, Base right) =>
                    object.ReferenceEquals(left, right);

                public static Derived CreateDerived() => new();

                public static Base Echo(object value) => (Base)value;

                public static T Create<T>() where T : Base, new() => new T();
            }

            public static class Workload
            {
                public static int Run()
                {
                    var derived = new Derived();
                    Base asBase = derived;
                    var result = 0;

                    Func<Base, Base, bool> exact = Methods.SameBase;
                    if (exact(asBase, asBase))
                    {
                        result |= 1;
                    }

                    Func<Derived, Derived, bool> parameters = object.ReferenceEquals;
                    if (parameters(derived, derived))
                    {
                        result |= 2;
                    }

                    Func<Base> returns = Methods.CreateDerived;
                    if (returns() is Derived)
                    {
                        result |= 4;
                    }

                    Func<Derived, Base> combined = Methods.Echo;
                    if (object.ReferenceEquals(combined(derived), derived))
                    {
                        result |= 8;
                    }

                    var receiver = new Receiver();
                    Func<Derived, bool> closed = receiver.Accept;
                    if (closed(derived))
                    {
                        result |= 16;
                    }

                    Receiver virtualReceiver = new DerivedReceiver();
                    Func<Derived, bool> virtualClosed = virtualReceiver.VirtualAccept;
                    if (virtualClosed(derived))
                    {
                        result |= 32;
                    }

                    Func<Base> generic = Methods.Create<Derived>;
                    if (generic() is Derived)
                    {
                        result |= 64;
                    }

                    return result;
                }
            }
            """;
        const string applicationSource = """
            using DelegateBindingLibrary;

            namespace DelegateBindingApplication;

            public static class EntryPoint
            {
                public static int Run(int input) => Workload.Run();
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource(
                "DelegateBindingLibrary",
                librarySource)
            : assets.CompileSource(
                "DelegateBindingLibrary",
                librarySource);
        var application = optimize
            ? assets.CompileOptimizedSource(
                "DelegateBindingApplication",
                applicationSource,
                library)
            : assets.CompileSource(
                "DelegateBindingApplication",
                applicationSource,
                library);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            application,
            [assets.CoreLib, library],
            "DelegateBindingApplication.EntryPoint",
            "Run",
            [],
            target));

        Assert.Equal(127, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd));
    }
}
