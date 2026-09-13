using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodBodyReaderFactory
{
    IMethodBodyReader Create(MetadataCompilationSnapshot snapshot);
}
