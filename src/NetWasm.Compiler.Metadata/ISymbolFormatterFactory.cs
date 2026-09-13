using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ISymbolFormatterFactory
{
    ISymbolFormatter Create(MetadataCompilationSnapshot snapshot);
}
