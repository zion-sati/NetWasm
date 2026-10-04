namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryCilBuilder
{
    // Consumes the evaluated public API arguments, leaving one query result.
    AttributeCilFragment Build(AttributeQueryCilRequest request);
}
