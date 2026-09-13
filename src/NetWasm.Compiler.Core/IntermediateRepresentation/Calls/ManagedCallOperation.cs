namespace NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

public enum ManagedCallOperation
{
    Direct,
    Virtual,
    Construct,
    LoadFunction,
    LoadVirtualFunction,
}
