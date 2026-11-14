using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>Plans the array, null and integer check proofs of a body, for the instructions it has.</summary>
internal interface IMethodBodyProver
{
    CilMethodBody Prove(CilMethodBody body);
}
