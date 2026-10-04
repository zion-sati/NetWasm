using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorTargetResolver
{
    UnsafeAccessorBinding Resolve(MethodInstanceModel accessor);
}
