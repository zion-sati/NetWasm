using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeValueDecoder
{
    CustomAttributeValue<CliTypeIdentity> Decode(CustomAttributeDescriptor attribute);
}
