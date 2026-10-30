using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class InheritedMethodReferenceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PrecompiledCallerUsesInheritedReplacementMethods(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource("Inherited.Library", """
            namespace Inherited;
            public class Base<T>
            {
                protected virtual T Echo(T value) => value;
                protected virtual U Identity<U>(U value) => value;
            }
            public class Middle<T> : Base<T>
            {
                protected override T Echo(T value) => base.Echo(value);
                protected override U Identity<U>(U value) => base.Identity(value);
            }
            """);
        var application = assets.CompileSource("Inherited.Application", """
            using Inherited;
            public sealed class Caller : Middle<int>
            {
                public int Execute(int value) => base.Echo(value) + base.Identity<int>(1);
            }
            public static class EntryPoint
            {
                public static int Run(int value) => new Caller().Execute(value);
            }
            """, library);
        library = assets.CompileSource("Inherited.Library", """
            namespace Inherited;
            public class Base<T>
            {
                protected virtual T Echo(T value) => value;
                protected virtual U Identity<U>(U value) => value;
            }
            public class Middle<T> : Base<T> { }
            """);
        var result = NetWasmCompiler.Compile(new(application, [assets.CoreLib, library],
            "EntryPoint", "Run", [new("run", "EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }
}
