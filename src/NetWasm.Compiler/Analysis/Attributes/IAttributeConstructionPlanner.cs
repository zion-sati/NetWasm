using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeConstructionPlanner
{
    AttributeConstructionPlan Plan(CustomAttributeDescriptor attribute);
}
