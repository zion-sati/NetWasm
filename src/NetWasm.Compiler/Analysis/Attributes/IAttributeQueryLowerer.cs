using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeQueryLowerer
{
    CilMethodBody Lower(CilMethodBody body);
}
