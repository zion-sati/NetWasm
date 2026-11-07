using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static DelegateEmitterTestSupport;
using static EmitterTestSupport;

public sealed class DelegateInvokeSignatureTests
{
    // A value-type result is written through a hidden first parameter, which moves the
    // delegate to the second; a scalar result is kept in a local of its kind; a void or
    // value-type result keeps an unused int32 local in its place.
    [Theory]
    [InlineData(CliValueKind.Void, false, false, CliValueKind.I4, 0)]
    [InlineData(CliValueKind.I8, false, true, CliValueKind.I8, 0)]
    [InlineData(CliValueKind.ManagedReference, false, true, CliValueKind.ManagedReference, 0)]
    [InlineData(CliValueKind.ValueType, true, false, CliValueKind.I4, 1)]
    public void ReadsTheResultShapeAndReceiverFromTheInvoke(
        CliValueKind result, bool returnsValue, bool returnsScalar, CliValueKind resultType, int receiver)
    {
        var invoke = CreateInvoke(CallbackType(), result == CliValueKind.ValueType
            ? CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true)
            : CliTypeIdentity.FromStackKind(result));

        var signature = DelegateInvokeSignature.Of(invoke);

        Assert.Equal(new DelegateInvokeSignature(returnsValue, returnsScalar, resultType), signature);
        Assert.Equal(receiver, signature.Receiver);
        Assert.Equal(receiver + 1, signature.FirstArgument);
    }
}
