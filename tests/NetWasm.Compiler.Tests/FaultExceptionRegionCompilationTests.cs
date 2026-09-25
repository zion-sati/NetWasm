using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class FaultExceptionRegionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void FaultPreservesPendingExceptionAcrossNestedCatch(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("NestedFaultFixture",
            """
            namespace NestedFaultFixture;
            public sealed class Original : System.Exception;
            public sealed class Inner : System.Exception;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var original = new Original();
                    try
                    {
                        try { if (input == 0) throw original; }
                        finally
                        {
                            try { throw new Inner(); }
                            catch (Inner) { input = 42; }
                        }
                    }
                    catch (Original observed)
                    {
                        return System.Object.ReferenceEquals(original, observed) ? input : -1;
                    }
                    return input;
                }
            }
            """, "10.0.302", "latest", optimize: optimize);
        MethodBodyPatcher.RewriteExceptionRegionKind(assembly, "NestedFaultFixture.EntryPoint", "Run",
            CilExceptionRegionKind.Finally, CilExceptionRegionKind.Fault);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "NestedFaultFixture.EntryPoint", "Run", []) { Target = target });
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0,
            target, result.StaticDataEnd, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            null, expectedEnvironmentReads: 0, expectedPreopenReads: 0));
        Assert.Equal(7, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7,
            target, result.StaticDataEnd, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
            null, expectedEnvironmentReads: 0, expectedPreopenReads: 0));
    }

    [Fact]
    public void FaultHandlerRunsOnlyForExceptionalExit()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "FaultRegionFixture",
            """
            namespace FaultRegionFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try
                    {
                        try
                        {
                            if (input == 0)
                            {
                                throw new System.Exception();
                            }
                        }
                        finally
                        {
                            input++;
                        }
                    }
                    catch (System.Exception)
                    {
                        return input;
                    }
                    return input;
                }
            }
            """);
        MethodBodyPatcher.RewriteExceptionRegionKind(
            assembly,
            "FaultRegionFixture.EntryPoint",
            "Run",
            CilExceptionRegionKind.Finally,
            CilExceptionRegionKind.Fault);

        using (var metadata = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]))
        {
            var snapshot = metadata.Snapshot;
            var methodFinder = MetadataCompilationTestActors.MethodFinder(snapshot);
            var methodBodies = MetadataCompilationTestActors.MethodBodies(snapshot);
            var method = methodFinder.FindMethod(
                snapshot.EntryAssemblyIdentity,
                "FaultRegionFixture.EntryPoint",
                "Run");
            Assert.Contains(
                methodBodies.ReadMethodBody(method).ExceptionRegions,
                region => region.Kind == CilExceptionRegionKind.Fault);
        }

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FaultRegionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(41, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }
}
