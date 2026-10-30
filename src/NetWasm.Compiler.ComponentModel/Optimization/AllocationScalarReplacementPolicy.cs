namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum AllocationScalarReplacementPolicy
{
    Disabled,
    // An object a method allocates and only reaches through locals, to read
    // and write its fields, becomes a local per field.
    NonEscapingObjects,
}
