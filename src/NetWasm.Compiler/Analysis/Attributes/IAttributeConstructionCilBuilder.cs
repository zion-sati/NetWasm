using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeConstructionCilBuilder
{
    // Consumes no query arguments and leaves one constructed attribute on the
    // evaluation stack. Instruction offsets are relative to this fragment.
    AttributeCilFragment Build(AttributeConstructionPlan attribute, int firstLocal);
}
