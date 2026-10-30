namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopRotationPolicy
{
    Disabled,
    // As Guarded, but only when the condition makes no call. Repeating a call
    // gives its callee a second call site, and the size presets' Binaryen
    // inlining keeps only functions called once.
    CallFree,
    // A loop tests its condition once before entry and again at the bottom
    // of each iteration, so the body falls through and only the back edge
    // is taken.
    Guarded,
}
