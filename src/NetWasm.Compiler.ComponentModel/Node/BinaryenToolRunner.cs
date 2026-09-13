using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Compiler.ComponentModel.Node;

public static class BinaryenToolIds
{
    public const string WasmOpt = "wasm-opt";
    public const string WasmMerge = "wasm-merge";

    public static ImmutableArray<string> Required { get; } = [WasmOpt, WasmMerge];
}

public sealed record BinaryenToolScript(string ToolId, string AbsolutePath);

public sealed record BinaryenToolRunnerConfiguration(
    string NodePath,
    ImmutableArray<BinaryenToolScript> Scripts);

public interface IBinaryenToolRunner
{
    ToolResult Run(string toolId, ImmutableArray<string> arguments);
}

public sealed class BinaryenToolRunner : IBinaryenToolRunner
{
    private readonly string _nodePath;
    private readonly ImmutableDictionary<string, string> _scripts;
    private readonly ISystemNodeCommandRunner _node;

    public BinaryenToolRunner(
        BinaryenToolRunnerConfiguration configuration,
        ISystemNodeCommandRunner node)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(node);
        RequireAbsolutePath(configuration.NodePath, nameof(configuration));
        _nodePath = configuration.NodePath;
        _scripts = CreateScriptMap(configuration.Scripts);
        _node = node;
    }

    public ToolResult Run(string toolId, ImmutableArray<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (arguments.IsDefault)
        {
            throw new ArgumentException(
                "Binaryen command arguments must be explicit.",
                nameof(arguments));
        }

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
        }

        if (!_scripts.TryGetValue(toolId, out var scriptPath))
        {
            throw new UnsupportedBinaryenToolException(toolId);
        }

        return _node.Run(new(_nodePath, scriptPath, arguments)) ??
            throw new InvalidOperationException(
                "The system Node runner returned no Binaryen result.");
    }

    private static ImmutableDictionary<string, string> CreateScriptMap(
        ImmutableArray<BinaryenToolScript> scripts)
    {
        if (scripts.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Binaryen tool scripts must be configured.",
                nameof(scripts));
        }

        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var script in scripts)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentException.ThrowIfNullOrWhiteSpace(script.ToolId);
            if (!BinaryenToolIds.Required.Contains(script.ToolId,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Unsupported Binaryen tool ID '{script.ToolId}'.",
                    nameof(scripts));
            }

            RequireAbsolutePath(script.AbsolutePath, nameof(scripts));
            if (result.ContainsKey(script.ToolId))
            {
                throw new ArgumentException(
                    $"Duplicate Binaryen tool ID '{script.ToolId}'.",
                    nameof(scripts));
            }

            result.Add(script.ToolId, script.AbsolutePath);
        }

        if (result.Count != BinaryenToolIds.Required.Length)
        {
            throw new ArgumentException(
                "Both wasm-opt and wasm-merge scripts must be configured.",
                nameof(scripts));
        }

        return result.ToImmutable();
    }

    private static void RequireAbsolutePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "System Node and Binaryen script paths must be absolute.",
                parameterName);
        }
    }
}

public sealed class UnsupportedBinaryenToolException(string toolId) :
    InvalidOperationException($"Binaryen tool '{toolId}' is not configured.")
{
    public string ToolId { get; } = toolId;
}
