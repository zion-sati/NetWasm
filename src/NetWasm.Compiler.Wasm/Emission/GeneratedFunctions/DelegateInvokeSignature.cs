using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// What a delegate's invoke functions read from its Invoke signature. A value-type
// result is written through a hidden first parameter, so the delegate is the second
// parameter then; any other result is kept in a local of its kind.
internal sealed record DelegateInvokeSignature(bool ReturnsValue, bool ReturnsScalar, CliValueKind ResultType)
{
    public int Receiver => ReturnsValue ? 1 : 0;

    public int FirstArgument => Receiver + 1;

    public static DelegateInvokeSignature Of(MethodInstanceModel invoke)
    {
        var returnsValue = invoke.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var returnsScalar = !returnsValue && invoke.Signature.ReturnType != CliValueKind.Void;
        return new(returnsValue, returnsScalar, returnsScalar ? invoke.Signature.ReturnType : CliValueKind.I4);
    }
}
