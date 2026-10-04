using System.Reflection.Metadata;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorDeclarationReader
{
    UnsafeAccessorDeclaration? Read(MetadataReader metadata, MethodDefinition method);
}
