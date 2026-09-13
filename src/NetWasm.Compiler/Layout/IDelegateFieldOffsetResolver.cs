namespace NetWasm.Compiler.Layout;

internal interface IDelegateFieldOffsetResolver
{
    ManagedDelegateFieldOffsets Resolve();
}
