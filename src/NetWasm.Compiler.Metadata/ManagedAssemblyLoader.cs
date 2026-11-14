using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

using System;

public sealed class ManagedAssemblyLoader(
    IManagedAssemblyImageReader images,
    IValueTypeDefinitionStackKindResolver stackKinds,
    INativeImportDeclarationReader nativeImports,
    INativeCallbackDeclarationReader nativeCallbacks) : IManagedAssemblyLoader
{
    private readonly IManagedAssemblyImageReader _images = images ??
        throw new ArgumentNullException(nameof(images));
    private readonly IValueTypeDefinitionStackKindResolver _stackKinds = stackKinds ?? throw new ArgumentNullException(nameof(stackKinds));
    private readonly INativeImportDeclarationReader _nativeImports = nativeImports ??
        throw new ArgumentNullException(nameof(nativeImports));
    private readonly INativeCallbackDeclarationReader _nativeCallbacks = nativeCallbacks ??
        throw new ArgumentNullException(nameof(nativeCallbacks));

    public ManagedAssembly Load(string path) =>
        Load(path, ImmutableDictionary<string, string>.Empty);

    public ManagedAssembly Load(
        string path,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? typeForwarders = null,
        INativeImportBinder? nativeBinding = null) =>
        ManagedAssembly.Parse(
            path,
            _images.Read(path),
            new AssemblyIdentityAliases(referenceAssemblyAliases, typeForwarders),
            _stackKinds,
            _nativeImports,
            _nativeCallbacks,
            nativeBinding);
}
