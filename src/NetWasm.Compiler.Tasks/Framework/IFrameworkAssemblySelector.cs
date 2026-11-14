namespace NetWasm.Compiler.Tasks.Framework;

// The NetWasm.App assemblies a program reaches, and the program assemblies they replace. A reference to a
// metadata facade (netstandard.dll) reaches the assemblies the facade forwards to.
internal interface IFrameworkAssemblySelector
{
    FrameworkAssemblySelection Select(
        IReadOnlyList<string> programAssemblies, IReadOnlyList<string> frameworkAssemblies, IReadOnlyList<string> facades);
}

// Framework: the framework assemblies the compiler gets. Displaced: the program assemblies (a package's copy
// of a framework assembly) the compiler does not get, because the framework's version is at least theirs.
internal sealed record FrameworkAssemblySelection(IReadOnlyList<string> Framework, IReadOnlyList<string> Displaced);
