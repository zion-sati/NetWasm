using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class DelegateSignatureComparer
{
    public static bool AreEqual(
        MethodSignatureModel target,
        MethodSignatureModel invoke) =>
        target.ReturnSignatureType.Equals(invoke.ReturnSignatureType) &&
        target.ParameterSignatureTypes.SequenceEqual(invoke.ParameterSignatureTypes);
}
