using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeQueryOperation
{
    IsDefined,
    GetOne,
    GetMany,
}

internal sealed record AttributeQueryCilRequest(
    AttributeQueryOperation Operation,
    AttributeQueryEntry Selection,
    int ArgumentCount,
    int? InheritArgument,
    bool? InheritConstant,
    CliTypeIdentity ResultArrayType,
    MethodInstanceModel? AmbiguityConstructor,
    int FirstLocal);
