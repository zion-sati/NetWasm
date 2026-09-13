using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeClassifierFactory
{
    ITypeClassifier Create(MetadataCompilationSnapshot snapshot);
}
