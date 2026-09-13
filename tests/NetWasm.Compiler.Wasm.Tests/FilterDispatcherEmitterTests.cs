using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FilterDispatcherEmitterTests
{
    private static readonly StructuredMethod FilterMethod = CreateFilterMethod();
    private static readonly StructuredExceptionClause FilterClause = new(
        CilExceptionRegionKind.Filter,
        0,
        1,
        null,
        0,
        StructuredSequence.Empty,
        StructuredSequence.Empty)
    {
        HandlerBlock = new StructuredBlockId(0),
        FilterBlock = new StructuredBlockId(0),
    };

    [Fact]
    public void EmptyDispatcherRejectsUnknownFilterId()
    {
        var body = CreateEmitter().Emit(
            [],
            ImmutableDictionary<int, int>.Empty);

        Assert.Equal(
            [0, WasmOpcodes.I32Constant, 0, WasmOpcodes.End],
            body);
    }

    [Fact]
    public void DispatcherOrdersFiltersAndCallsTheirMappedFunctions()
    {
        var body = CreateEmitter().Emit(
            [new FilterFunclet(7,
            new ManagedMethodIdentity("test-filter"), FilterMethod, FilterClause), new FilterFunclet(3,
            new ManagedMethodIdentity("test-filter"), FilterMethod, FilterClause)],
            ImmutableDictionary<int, int>.Empty
                .Add(3, 20)
                .Add(7, 40));

        Assert.True(IndexOf(body, WasmOpcodes.I32Constant, 3) <
                    IndexOf(body, WasmOpcodes.I32Constant, 7));
        Assert.Contains(
            Enumerable.Range(0, body.Length - 1),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 20);
        Assert.Contains(
            Enumerable.Range(0, body.Length - 1),
            index => body[index] == WasmOpcodes.Call && body[index + 1] == 40);
    }

    private static IFilterDispatcherEmitter CreateEmitter() =>
        new[] { new FilterDispatcherEmitter(new GeneratedFunctionWriterFactory()) }
            .Cast<IFilterDispatcherEmitter>()
            .Single();

    private static StructuredMethod CreateFilterMethod()
    {
        var program = new FakeProgram();
        return EmitterTestSupport.Structure(
            program,
            program.GetMethod(EmitterTestSupport.EntryKey),
            EmitterTestSupport.I(
                0,
                CilOperation.LoadInt32,
                new CilOperand.ConstantI4(0)),
            EmitterTestSupport.I(1, CilOperation.Return));
    }

    private static int IndexOf(byte[] body, byte opcode, byte operand)
    {
        for (var index = 0; index < body.Length - 1; index++)
        {
            if (body[index] == opcode && body[index + 1] == operand)
            {
                return index;
            }
        }

        return -1;
    }
}
