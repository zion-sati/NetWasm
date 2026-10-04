using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeValueKind
{
    Unknown,
    TypeToken,
    Type,
    Null,
    Int32,
}

internal readonly record struct AttributeValueProof(
    AttributeValueKind Kind,
    CliTypeIdentity? Type = null,
    int Integer = 0)
{
    public bool? Condition => Kind switch
    {
        AttributeValueKind.Null => false,
        AttributeValueKind.Type => true,
        AttributeValueKind.Int32 => Integer != 0,
        _ => null,
    };
}
