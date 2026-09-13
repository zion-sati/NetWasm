using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

using System;

public sealed class ManagedAssemblyLoader(
    IManagedAssemblyImageReader images,
    IValueTypeDefinitionStackKindResolver stackKinds) : IManagedAssemblyLoader
{
    private readonly IManagedAssemblyImageReader _images = images ??
        throw new ArgumentNullException(nameof(images));
    private readonly IValueTypeDefinitionStackKindResolver _stackKinds = stackKinds ?? throw new ArgumentNullException(nameof(stackKinds));

    public ManagedAssembly Load(string path) =>
        Load(path, ImmutableDictionary<string, string>.Empty);

    public ManagedAssembly Load(
        string path,
        ImmutableDictionary<string, string> referenceAssemblyAliases) =>
        ManagedAssembly.Parse(
            path,
            _images.Read(path),
            new AssemblyIdentityAliases(referenceAssemblyAliases), _stackKinds);
}
