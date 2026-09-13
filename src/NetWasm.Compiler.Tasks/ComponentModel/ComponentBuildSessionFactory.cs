using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class ComponentBuildSessionFactory(
    Func<ExternalToolCommand, BinaryenToolRunnerConfiguration,
        IComponentBuildSession> create) : IComponentBuildSessionFactory
{
    private readonly Func<ExternalToolCommand, BinaryenToolRunnerConfiguration,
        IComponentBuildSession> _create =
        create ?? throw new ArgumentNullException(nameof(create));

    public IComponentBuildSession Create(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        ArgumentNullException.ThrowIfNull(binaryenConfiguration);
        return _create(wasmToolsCommand, binaryenConfiguration);
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
