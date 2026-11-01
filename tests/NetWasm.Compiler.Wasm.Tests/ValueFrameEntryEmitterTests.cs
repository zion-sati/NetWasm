using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ValueFrameEntryEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BindsColdScalarAddressesWithoutEnteringRootOrTraceFrames(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var context = CreateMethodEmissionContext() with
        {
            StackTraceMethodId = 17,
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Argument, 0), 0), []),
            ValueLayout = new(16, ImmutableDictionary<int, int>.Empty.Add(0, 0).Add(1, 8), [], [], []),
            AddressTakenScalarPromotions = new(new(ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type), []),
                ImmutableDictionary<int, int>.Empty.Add(0, 101)),
        };
        var header = fixture.Header with
        {
            Locals = [CliValueKind.I8, CliValueKind.ValueType],
            LocalSignatureTypes = [type, CliTypeIdentity.Named(Assembly, "Test", "Storage", true)],
        };

        fixture.Emitter.Emit(fixture.Code, header, context);

        var instructions = fixture.Code.ToInstructions();
        var call = Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.Call);
        Assert.Equal((uint)fixture.Imports.Resolve(RuntimeImportSymbol.ValueFrameEnter), call.Operand.UnsignedValue);
        Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            instructions[0].Opcode);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[2].Opcode);
        Assert.Equal((uint)context.ValueFrame, instructions[2].Operand.UnsignedValue);
        Assert.Equal(WasmOpcodes.I32Constant, instructions[3].Opcode);
        Assert.Equal(WasmOpcodes.Drop, instructions[4].Opcode);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 101);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == (uint)(context.LocalBase + 1));
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == (uint)context.LocalBase);
        Assert.Equal(1, fixture.Initialization.Calls);
        Assert.Same(context, fixture.Initialization.Context);
    }

    // A frame that holds references passes its map: the word count and first
    // word to the runtime, any later nonzero word stored after the frame's
    // bytes, which the runtime rounds up to sixteen.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AFrameThatHoldsReferencesPassesItsMap(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new(40, [], [], [], []),
            ValueFrameReferences = new([0b1010, 0, 1UL << 3], [(false, 0)]),
        };

        fixture.Emitter.Emit(fixture.Code, fixture.Header, context);

        var instructions = fixture.Code.ToInstructions();
        Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, instructions[0].Opcode);
        Assert.Equal((WasmOpcodes.I32Constant, 3L), (instructions[1].Opcode, instructions[1].Operand.SignedValue));
        Assert.Equal((WasmOpcodes.I64Constant, 0b1010L), (instructions[2].Opcode, instructions[2].Operand.Signed64Value));
        Assert.Equal((WasmOpcodes.Call, (uint)fixture.Imports.Resolve(RuntimeImportSymbol.ValueFrameEnterMapped)),
            (instructions[3].Opcode, instructions[3].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.LocalSet, (uint)context.ValueFrame), (instructions[4].Opcode, instructions[4].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.LocalGet, (uint)context.ValueFrame), (instructions[5].Opcode, instructions[5].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.I64Constant, 8L), (instructions[6].Opcode, instructions[6].Operand.Signed64Value));
        Assert.Equal(WasmOpcodes.I64Store, instructions[7].Opcode);
        Assert.Equal(48u + 16u, instructions[7].Operand.Offset);
        Assert.Equal(WasmOpcodes.I32Constant, instructions[8].Opcode);
        Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.Call);
    }

    [Fact]
    public void EmptyFrameDoesNotAllocateInitializeOrTouchMethodRoots()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        fixture.Emitter.Emit(fixture.Code, fixture.Header, CreateMethodEmissionContext() with
        {
            ValueLayout = new(0, [], [], [], []),
            StackTraceMethodId = 17,
        });
        Assert.Empty(fixture.Code.ToInstructions());
        Assert.Equal(0, fixture.Initialization.Calls);
    }

    [Fact]
    public void RejectsMissingInputsBeforeWritingAnything()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var context = CreateMethodEmissionContext();
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(null!, fixture.Header, context));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, null!, context));
        Assert.Throws<ArgumentNullException>(() => fixture.Emitter.Emit(fixture.Code, fixture.Header, null!));
        Assert.Empty(fixture.Code.ToInstructions());
        Assert.Equal(0, fixture.Initialization.Calls);
    }

    private sealed class Fixture
    {
        public Fixture(WasmTarget target)
        {
            var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
            var program = new FakeProgram();
            Header = new(program.GetMethod(EntryKey), null, 1, [], [], []);
            Emitter = new[] { new ValueFrameEntryEmitter(layouts, layouts, CreateArgumentSignatureTypes(program),
                Imports, CreateValueFrameAddresses(layouts), Initialization,
                new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts)),
                new ManagedAggregateValueInitializer(layouts)) }.Cast<IValueFrameEntryEmitter>().Single();
        }

        public IValueFrameEntryEmitter Emitter { get; }
        public IRuntimeImportResolver Imports { get; } = new[] { WasmRuntimeImports.CreateCatalog() }
            .Cast<IRuntimeImportResolver>().Single();
        public StructuredMethodHeader Header { get; }
        public RecordingInstructionWriter Code { get; } = new();
        public RecordingInitialization Initialization { get; } = new();
    }

    private sealed class RecordingInitialization : IValueFrameInitializationEmitter
    {
        public int Calls { get; private set; }
        public MethodEmissionContext? Context { get; private set; }

        public void Initialize(IWasmInstructionWriter code, StructuredMethodHeader header, MethodEmissionContext context)
        {
            Calls++;
            Context = context;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        }
    }
}
