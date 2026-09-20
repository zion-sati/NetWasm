using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Environment;

public interface IHostToolsExecutableChooser
{
    ResolvedHostExecutable Choose(ResolvedHostExecutable packaged);
}

public sealed class HostToolsOverrideSelector(
    IHostEnvironmentVariableReader environment,
    IHostExecutablePathResolver executablePaths) : IHostToolsExecutableChooser
{
    private readonly IHostEnvironmentVariableReader _environment = environment ??
        throw new ArgumentNullException(nameof(environment));
    private readonly IHostExecutablePathResolver _executablePaths = executablePaths ??
        throw new ArgumentNullException(nameof(executablePaths));

    public ResolvedHostExecutable Choose(ResolvedHostExecutable packaged)
    {
        ArgumentNullException.ThrowIfNull(packaged);
        var (name, overrideVariable) = packaged.ToolId switch
        {
            HostToolIds.Node => ("node", "NETWASM_NODE_PATH"),
            HostToolIds.WasmLd => ("wasm-ld", "NETWASM_WASM_LD_PATH"),
            HostToolIds.BinaryenWasmOpt => ("wasm-opt", "NETWASM_WASM_OPT_PATH"),
            HostToolIds.BinaryenWasmMerge => ("wasm-merge", "NETWASM_WASM_MERGE_PATH"),
            _ => throw new ArgumentException($"Unknown host tool: {packaged.ToolId}", nameof(packaged)),
        };

        var explicitPath = _environment.Read(overrideVariable);
        if (explicitPath is not null)
        {
            return _executablePaths.Resolve(new(
                packaged.ToolId, name, overrideVariable, [], [], SearchPath: false));
        }

        if (packaged.ToolId == HostToolIds.Node)
        {
            return packaged;
        }

        var explicitRoot = _environment.Read("NETWASM_EMSDK_ROOT");
        if (explicitRoot is null)
        {
            return packaged;
        }
        if (string.IsNullOrWhiteSpace(explicitRoot))
        {
            throw new ArgumentException("NETWASM_EMSDK_ROOT must be an absolute, nonempty directory.");
        }
        return _executablePaths.Resolve(new(
            packaged.ToolId,
            name,
            overrideVariable,
            [],
            [new HostExecutableRootFallback("NETWASM_EMSDK_ROOT", Path.Combine("upstream", "bin"))],
            SearchPath: false));
    }
}
