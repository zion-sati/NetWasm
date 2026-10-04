using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorBodyBuilder
{
    CilMethodBody Build(MethodInstanceModel accessor, UnsafeAccessorBinding binding);
}
