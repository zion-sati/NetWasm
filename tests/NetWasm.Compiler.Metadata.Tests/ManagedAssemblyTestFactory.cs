namespace NetWasm.Compiler.Metadata.Tests;

internal static class ManagedAssemblyTestFactory
{
    internal static ManagedAssembly Load(string path) =>
        new ManagedAssemblyLoader(
            new ManagedAssemblyImageReader(),
            new ValueTypeDefinitionStackKindResolver()).Load(path);
}
