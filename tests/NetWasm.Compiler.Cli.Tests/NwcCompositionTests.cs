using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Cli.Tests;

// nwc's choices of what it composes: each target's entry point kind, and wasm-tools cached
// where the host keeps caches.
public sealed class NwcCompositionTests
{
    [Fact]
    public void EveryTargetHasAnEntryPointKind()
    {
        Assert.Equal(Enum.GetValues<NwcTarget>().Order(), CompileCliCommand.EntryPointKinds.Keys.Order());
        Assert.Equal(CompilerEntryPointKind.RawFunction, CompileCliCommand.EntryPointKinds[NwcTarget.Function]);
    }

    [Fact]
    public void WasmToolsAreCachedOnlyWhereTheHostHasACacheDirectory()
    {
        var tools = new NoTools();
        var command = new ExternalToolCommand("wasm-tools", []);

        Assert.Same(tools, NwcCompilerComposition.Tools(tools, command, null));
        Assert.IsType<CachingWasmTools>(NwcCompilerComposition.Tools(tools, command,
            Path.Combine(Path.GetTempPath(), "netwasm-cache-" + Guid.NewGuid().ToString("N"))));
    }

    private sealed class NoTools : IWasmTools
    {
        public ToolResult Run(params IEnumerable<string> arguments) => throw new NotSupportedException();
    }
}
