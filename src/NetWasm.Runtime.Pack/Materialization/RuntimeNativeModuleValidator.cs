using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeModuleValidator(
    IRuntimeNativeValidationArgumentBuilder arguments,
    ICommandInvoker commands) : IRuntimeNativeModuleValidator
{
    private readonly IRuntimeNativeValidationArgumentBuilder _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly ICommandInvoker _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public void Validate(RuntimeNativeModuleValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NodePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CommandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LogPath);
        var arguments = _arguments.Build(request.Profile, request.Target, request.Path);
        _commands.Invoke(new(request.NodePath,
            ["--disable-warning=ExperimentalWarning", request.CommandPath, request.ModulePath, .. arguments], request.LogPath));
    }
}
