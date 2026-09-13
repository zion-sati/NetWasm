namespace NetWasm.Compiler.Layout;

internal sealed record ManagedDelegateFieldOffsets(
    int Target,
    int MethodId,
    int Left,
    int Right);
