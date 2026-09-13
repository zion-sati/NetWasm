using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataSymbolFormatter(
    ITypeRepository types) : ISymbolFormatter
{
    public string Format(EntityKey key) => types.GetTypeDefinition(key).FullName;

    public string Format(MethodDefinitionModel method) =>
        $"{Format(method.DeclaringType)}::{method.Name}";
}
