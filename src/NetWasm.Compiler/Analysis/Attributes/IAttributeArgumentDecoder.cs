using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeArgumentDecoder
{
    AttributeArgumentPlan Decode(CustomAttributeTypedArgument<CliTypeIdentity> argument,
        CliTypeIdentity destination);
}
