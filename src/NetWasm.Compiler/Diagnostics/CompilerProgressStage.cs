namespace NetWasm.Compiler.Diagnostics;

public enum CompilerProgressStage
{
    Start = 0,
    Metadata = 1,
    Entry = 2,
    Analysis = 3,
    Layouts = 4,
    RootMaps = 5,
    Emission = 6,
    Complexity = 7,
    Validation = 8,
    InteropManifest = 9,
    Complete = 10,
}
