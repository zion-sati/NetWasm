namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorMemberResolver
{
    UnsafeAccessorMemberMatch Resolve(UnsafeAccessorMemberRequest request);
}
