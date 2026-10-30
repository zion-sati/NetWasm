namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ArrayLoopIdiomPolicy
{
    Disabled,
    // A counted loop that only stores one value into consecutive elements of
    // an array, only copies elements between two arrays at the same index, or
    // only folds array or span elements into locals with bitwise operations,
    // first does the iterations that stay within its arrays in bulk.
    Bulk,
}
