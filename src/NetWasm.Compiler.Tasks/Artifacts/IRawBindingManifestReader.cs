namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface IRawBindingManifestReader
{
    RawBindingManifest Read(string path, string target);
}
