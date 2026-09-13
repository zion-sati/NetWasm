namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface ICompilerBuildMetadataReader
{
    CompilerBuildMetadata Read(string path);
}
