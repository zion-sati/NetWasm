using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class AllocationResultValidatorTests
{
    [Fact]
    public void NullAllocationResultThrowsOutOfMemoryThroughAddressWidthStrategy()
    {
        var layouts = new RecordingLayoutProvider();
        var code = new RecordingInstructionWriter();
        var validator = ThroughContract(new AllocationResultValidator(
            CreateAddressInstructions(layouts),
            new ImplicitExceptionEmitter(layouts)));

        validator.Validate(code, 5, OptionalFunctionIndex.At(7));

        Assert.Equal(WasmOpcodes.LocalGet, code.ToArray()[0]);
        Assert.Contains(WasmOpcodes.I32EqualZero, code.ToArray());
        Assert.Contains(WasmOpcodes.If, code.ToArray());
        Assert.True(ContainsManagedThrow(code.ToArray()));
        Assert.Equal(WasmOpcodes.End, code.ToArray()[^1]);
    }

    private static IAllocationResultValidator ThroughContract(
        AllocationResultValidator validator) => new[]
        {
            validator,
        }.Cast<IAllocationResultValidator>().Single();
}
