namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationMaterializationFactory
{
    MetadataCompilationMaterialization Create(MetadataCompilationSnapshot snapshot);
}
