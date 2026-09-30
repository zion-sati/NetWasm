using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataPropertyAccessorResolver
{
    PropertyInstanceModel? Resolve(MethodInstanceModel accessor);
}
