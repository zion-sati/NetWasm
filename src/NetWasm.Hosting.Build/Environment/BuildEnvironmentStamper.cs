using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Environment;

/// <summary>
/// Summarizes what resolving a build environment reads: the request, the environment
/// variables that select tools, this assembly, and the size and write time of every
/// file in the restored packages. Equal stamps resolve to the same environment.
/// </summary>
public interface IBuildEnvironmentStamper
{
    string Stamp(HostingBuildEnvironmentRequest request);
}

public sealed class BuildEnvironmentStamper(IHostEnvironmentVariableReader environment) : IBuildEnvironmentStamper
{
    private static readonly ImmutableArray<string> SelectingVariables =
    [
        "PATH",
        "NETWASM_NODE_PATH",
        "NETWASM_WASM_LD_PATH",
        "NETWASM_WASM_OPT_PATH",
        "NETWASM_WASM_TOOLS_PATH",
        "NETWASM_EMSDK_ROOT",
    ];

    private readonly IHostEnvironmentVariableReader _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));

    public string Stamp(HostingBuildEnvironmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = new StringBuilder();
        Append(text, "resolver", typeof(BuildEnvironmentStamper).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
        Append(text, "toolchain", request.ToolchainPackageRoot);
        Append(text, "host-tools", request.HostToolsPackageRoot);
        Append(text, "host-tools-id", request.HostToolsPackageId);
        Append(text, "host-tools-version", request.HostToolsPackageVersion);
        Append(text, "host-rid", request.HostRid);
        foreach (var name in SelectingVariables)
        {
            Append(text, name, _environment.Read(name));
        }
        FileTreeStamp.AppendPath(text, request.ToolchainPackageRoot);
        FileTreeStamp.AppendPath(text, request.HostToolsPackageRoot);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Append(StringBuilder text, string name, string? value) =>
        FileTreeStamp.Append(text, name, value);
}
