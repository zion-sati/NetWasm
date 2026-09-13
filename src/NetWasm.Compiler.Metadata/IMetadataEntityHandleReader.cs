using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataEntityHandleReader
{
    EntityHandle Read(
        int token,
        string kind,
        string method,
        int ilOffset);
}
