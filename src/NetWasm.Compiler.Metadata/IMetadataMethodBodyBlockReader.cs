using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataMethodBodyBlockReader
{
    MethodBodyBlock Read(PEReader source, MethodDefinitionModel method);
}
