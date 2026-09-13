using System;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawModuleInspectionRequest(
    string NodePath,
    string ScriptPath,
    string ModulePath,
    string BinaryenPath);

public interface IRawModuleInspectionProcess
{
    ToolResult Run(RawModuleInspectionRequest request);
}

public sealed class RawModuleInspectionProcess(
    ISystemNodeCommandRunner node) : IRawModuleInspectionProcess
{
    private readonly ISystemNodeCommandRunner _node = node ??
        throw new ArgumentNullException(nameof(node));

    public ToolResult Run(RawModuleInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAbsolutePath(request.ModulePath, nameof(request.ModulePath));
        RequireAbsolutePath(request.BinaryenPath, nameof(request.BinaryenPath));
        return _node.Run(new(
            request.NodePath,
            request.ScriptPath,
            [request.ModulePath, request.BinaryenPath]));
    }

    private static void RequireAbsolutePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Raw module and Binaryen inspection paths must be absolute.",
                parameterName);
        }
    }
}
