using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FloatingIntrinsicEmitterTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.SingleToInt32Bits, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.Int32BitsToSingle, CliValueKind.I4)]
    [InlineData(RuntimeIntrinsic.DoubleToInt64Bits, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.Int64BitsToDouble, CliValueKind.I8)]
    [InlineData(RuntimeIntrinsic.FloatingAbsolute, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingAbsolute, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.FloatingCeiling, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingCeiling, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.FloatingFloor, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingFloor, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.FloatingRound, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingRound, CliValueKind.F8)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot, CliValueKind.F4)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot, CliValueKind.F8)]
    public void EmitsEveryFloatingIntrinsicThroughItsCapability(
        RuntimeIntrinsic intrinsic,
        CliValueKind argument)
    {
        var emitter = CreateEmitter(intrinsic);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, [argument]);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void RejectsNonFloatingUnaryArgument()
    {
        var emitter = new FloatingAbsoluteIntrinsicEmitter();
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.FloatingAbsolute,
            [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(emitter, request));
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.FloatingCeiling)]
    [InlineData(RuntimeIntrinsic.FloatingFloor)]
    [InlineData(RuntimeIntrinsic.FloatingTruncate)]
    [InlineData(RuntimeIntrinsic.FloatingRound)]
    [InlineData(RuntimeIntrinsic.FloatingSquareRoot)]
    public void RejectsNonFloatingArgumentForEveryUnaryFloatingCapability(
        RuntimeIntrinsic intrinsic)
    {
        var emitter = CreateEmitter(intrinsic);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, [CliValueKind.I4]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(emitter, request));
    }

    private static IRuntimeIntrinsicEmitter CreateEmitter(RuntimeIntrinsic intrinsic) =>
        intrinsic switch
        {
            RuntimeIntrinsic.SingleToInt32Bits => new SingleToInt32BitsIntrinsicEmitter(),
            RuntimeIntrinsic.Int32BitsToSingle => new Int32BitsToSingleIntrinsicEmitter(),
            RuntimeIntrinsic.DoubleToInt64Bits => new DoubleToInt64BitsIntrinsicEmitter(),
            RuntimeIntrinsic.Int64BitsToDouble => new Int64BitsToDoubleIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingAbsolute => new FloatingAbsoluteIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingCeiling => new FloatingCeilingIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingFloor => new FloatingFloorIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingTruncate => new FloatingTruncateIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingRound => new FloatingRoundIntrinsicEmitter(),
            RuntimeIntrinsic.FloatingSquareRoot => new FloatingSquareRootIntrinsicEmitter(),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));
}
