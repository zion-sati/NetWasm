using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryPlanner
{
    AttributeQueryEntry Plan(ResolvedAttributeQuery query);
}
