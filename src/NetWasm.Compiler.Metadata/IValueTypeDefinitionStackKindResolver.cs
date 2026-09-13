using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IValueTypeDefinitionStackKindResolver
{
    CliValueKind Resolve(string canonicalName);
}
