namespace NetWasm.Compiler.Tasks.Framework;

// An assembly's version and the simple names of the assemblies it references; null for a file without metadata.
internal interface IAssemblyMetadataReader
{
    AssemblyMetadataSummary? Read(string assemblyPath);
}

internal sealed record AssemblyMetadataSummary(Version Version, IReadOnlyList<string> References);
