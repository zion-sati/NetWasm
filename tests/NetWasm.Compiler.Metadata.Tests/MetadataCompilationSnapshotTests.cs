using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCompilationSnapshotTests
{
    [Fact]
    public void ExposesEachCompilationCollectionThroughItsPublicContract()
    {
        var assemblies = ImmutableArray<ManagedAssembly>.Empty;
        var types = ImmutableArray<TypeDefinitionModel>.Empty;
        var methods = ImmutableArray<MethodDefinitionModel>.Empty;
        var snapshot = new MetadataCompilationSnapshot(
            assemblies,
            new AssemblyIdentity("Tests"),
            types,
            methods,
            methods);

        Assert.Equal(assemblies, snapshot.Assemblies);
        Assert.Equal(types, snapshot.Types);
        Assert.Equal(methods, snapshot.Methods);
        Assert.Equal(methods, snapshot.EntryAssemblyMethods);
    }
}
