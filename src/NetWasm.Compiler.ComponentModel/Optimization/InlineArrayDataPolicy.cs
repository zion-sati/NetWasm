namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum InlineArrayDataPolicy
{
    // Element addresses start from the data pointer every array keeps.
    Disabled,
    // A one-dimensional array's elements are addressed at the inline offset
    // every runtime keeps them at, without reading its data pointer.
    Inline,
}
