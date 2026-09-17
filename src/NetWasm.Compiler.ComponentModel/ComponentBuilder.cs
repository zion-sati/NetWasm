using System;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentBuilder
{
    ComponentManifest Build(ComponentBuildRequest request);
}

public sealed class ComponentBuilder(
    IWitDocumentReader documents,
    IWitWorldValidator worlds,
    IComponentManifestBuilder manifests,
    IWitWorldSpecifierFormatter specifiers,
    IComponentPackager components) : IComponentBuilder
{
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitWorldValidator _worlds = worlds ??
        throw new ArgumentNullException(nameof(worlds));
    private readonly IComponentManifestBuilder _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));
    private readonly IWitWorldSpecifierFormatter _specifiers = specifiers ??
        throw new ArgumentNullException(nameof(specifiers));
    private readonly IComponentPackager _components = components ??
        throw new ArgumentNullException(nameof(components));

    public ComponentManifest Build(ComponentBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = _documents.Read(request.WitPath);
        var world = document.SelectWorld(request.World);
        _worlds.Validate(document, world);
        var manifest = _manifests.Build(
            document,
            world,
            request.Target,
            request.ManifestInputs);
        _components.Package(new(
            request.CoreModulePath,
            request.WitPath,
            _specifiers.Format(world),
            request.OutputPath,
            request.Target,
            request.RuntimeModulePath,
            request.ManagedExecutableEntryPoint,
            request.Optimization));
        return manifest;
    }
}
