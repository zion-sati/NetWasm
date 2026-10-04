using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorSignatureReader
{
    MethodSignature<CliTypeIdentity> Read(MethodDefinitionModel method);
    CliTypeIdentity Read(FieldDefinitionModel field);
}
