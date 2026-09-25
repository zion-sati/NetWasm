using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

internal sealed class CompilerInvariantTestContext : IDisposable
{
    private CompilerInvariantTestContext(
        TestAssets assets,
        IMetadataCompilationLease metadata,
        CompilationResult result)
    {
        Assets = assets;
        Metadata = metadata;
        Result = result;
    }

    public TestAssets Assets { get; }

    public IMetadataCompilationLease Metadata { get; }

    public CompilationResult Result { get; }

    public static CompilerInvariantTestContext Create(
        Func<CompilerOptions, CompilerOptions>? configureOptions = null)
    {
        var assets = TestAssets.Create();
        try
        {
            var options = CompilerTestSupport.Options(assets, []) with
            {
                WitPath = Path.Combine(
                    assets.Root,
                    "wit",
                    "netwasm-platform-1.0.0"),
                WitWorld = "netwasm:platform@1.0.0/platform",
            };
            if (configureOptions is not null)
                options = configureOptions(options);
            var result = NetWasmCompiler.Compile(options);
            var metadata = MetadataCompilationTestFactory.Load(
                assets.Application,
                [assets.Library, assets.CoreLib]);
            return new CompilerInvariantTestContext(assets, metadata, result);
        }
        catch
        {
            assets.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Metadata.Dispose();
        Assets.Dispose();
    }
}
