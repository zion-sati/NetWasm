using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

internal static class MetadataCompilationTestFactory
{
    internal static IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths) =>
        ((IMetadataCompilationLoader)CreateLoader())
            .Load(entryAssemblyPath, referencePaths);

    internal static IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths,
        System.Collections.Immutable.ImmutableDictionary<string, string> aliases) =>
        ((IMetadataCompilationLoader)CreateLoader())
            .Load(entryAssemblyPath, referencePaths, aliases);

    private static MetadataCompilationLoader CreateLoader() => new(
        new ManagedAssemblyLoader(new ManagedAssemblyImageReader(), new ValueTypeDefinitionStackKindResolver()),
        new ReferenceClosureValidator(),
        new MetadataCompilationFactory());
}
