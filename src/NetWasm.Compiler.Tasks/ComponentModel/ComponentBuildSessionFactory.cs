using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class ComponentBuildSessionFactory(
    Func<ExternalToolCommand, BinaryenToolRunnerConfiguration, ILoggerProvider,
        IComponentBuildSession> create) : IComponentBuildSessionFactory
{
    private readonly Func<ExternalToolCommand, BinaryenToolRunnerConfiguration, ILoggerProvider,
        IComponentBuildSession> _create =
        create ?? throw new ArgumentNullException(nameof(create));

    public IComponentBuildSession Create(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration,
        ILoggerProvider progress)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        ArgumentNullException.ThrowIfNull(binaryenConfiguration);
        ArgumentNullException.ThrowIfNull(progress);
        return _create(wasmToolsCommand, binaryenConfiguration, progress);
    }
}

internal sealed class ComponentBuildSession(
    ServiceProvider services,
    IComponentBuilder components) : IComponentBuildSession
{
    private readonly IComponentBuilder _components = components ??
        throw new ArgumentNullException(nameof(components));

    public ComponentManifest Build(ComponentBuildRequest request) =>
        _components.Build(request);

    public void Dispose() => services.Dispose();
}
