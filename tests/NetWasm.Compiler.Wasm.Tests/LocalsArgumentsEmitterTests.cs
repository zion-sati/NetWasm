using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class LocalsArgumentsEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void AuthoritativeReferencesWriteTheirSlotsAndReadTheirLocalCaches(WasmTarget target, bool argument)
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var source = new RootSource(argument ? RootSourceKind.Argument : RootSourceKind.Local, 0);
        var context = CreateContext() with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 2),
        };
        var body = CreateBody() with { LocalSignatureTypes = [type] };
        var stack = new List<CliValueKind> { CliValueKind.ManagedReference };
        var code = new RecordingInstructionWriter();
        var emitter = CreateEmitter(WasmTargetLayout.For(target), type);
        Emit(emitter, Request(body, argument ? CilOperation.StoreArgument : CilOperation.StoreLocal, 0, stack, context), code);
        Assert.Empty(stack);
        Emit(emitter, Request(body, argument ? CilOperation.LoadArgument : CilOperation.LoadLocal, 0, stack, context), code);
        Assert.Equal([CliValueKind.ManagedReference], stack);
        var instructions = code.ToInstructions();
        var store = Assert.Single(instructions.Where(i => i.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store)));
        Assert.DoesNotContain(instructions, i => i.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Load : WasmOpcodes.I64Load));
        Assert.Equal((uint)(2 * WasmTargetLayout.For(target).ObjectReferenceSize), store.Operand.Offset);
        var cache = (uint)(argument ? context.ParameterOffset : context.LocalBase);
        Assert.Contains(instructions, i => i.Opcode == WasmOpcodes.LocalTee && i.Operand.UnsignedValue == cache);
        Assert.Contains(instructions, i => i.Opcode == WasmOpcodes.LocalGet && i.Operand.UnsignedValue == cache);
        Assert.Equal(1, instructions.Count(i => i.Opcode == WasmOpcodes.LocalGet &&
            i.Operand.UnsignedValue == (uint)context.RootFrame));
    }

    // A stored authoritative byref argument writes its slot as it updates its local.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AStoredAuthoritativeByrefArgumentWritesItsSlot(WasmTarget target)
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var context = CreateContext() with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty
                .Add(new(RootSourceKind.ManagedAddressArgument, 0), 1),
        };
        var stack = new List<CliValueKind> { CliValueKind.ManagedAddress };
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(WasmTargetLayout.For(target), type),
            Request(CreateBody(), CilOperation.StoreArgument, 0, stack, context), code);

        Assert.Empty(stack);
        var instructions = code.ToInstructions();
        var store = Assert.Single(instructions.Where(i => i.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store)));
        Assert.Equal((uint)WasmTargetLayout.For(target).ObjectReferenceSize, store.Operand.Offset);
        Assert.Contains(instructions, i => i.Opcode == WasmOpcodes.LocalTee &&
            i.Operand.UnsignedValue == (uint)context.ParameterOffset);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SelectedDirectValueArgumentRequiresFrameBacking(WasmTarget target)
    {
        var type = CreateValueType();
        var storage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Argument, 0);
        var plan = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty
                .Add(storage, new(storage, type, 4, true)),
        };
        var context = CreateContext();
        context = context with
        {
            ValueLayout = context.ValueLayout with { ArgumentOffsets = [], DirectValueLoadOffsets = [0] },
            ManagedAggregateValues = new(plan, []),
        };
        var request = Request(CreateBody(), CilOperation.LoadArgument, 0, [], context);
        var writer = new RecordingInstructionWriter();

        Assert.Throws<InvalidOperationException>(() => Emit(CreateEmitter(WasmTargetLayout.For(target), type), request, writer));

        Assert.Empty(writer.ToInstructions());
        Assert.Empty(request.Stack);
    }

    [Fact]
    public void SelectedWholeValueArgumentLoadsFromStableBacking()
    {
        var valueType = CreateValueType();
        var storage = new ManagedAggregateStorageId(
            ManagedAggregateStorageKind.Argument,
            0);
        var lane = new ManagedAggregateSsaLane(storage, 0);
        var plan = new ManagedAggregateSsaPlan(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>
                .Empty.Add(storage, new(storage, valueType, sizeof(int), true)),
            [lane],
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
                .Empty.Add(lane, new(0)),
            [], [], [], [], [], []);
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                [],
                [])
            {
                DirectValueLoadOffsets = [0],
            },
            ManagedAggregateValues = new(
                plan,
                ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty.Add(lane, 30)),
        };
        var request = Request(
            CreateBody(),
            CilOperation.LoadArgument,
            0,
            [],
            context);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        var instructions = code.ToInstructions();
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.ValueFrame)),
            instructions[0]);
        Assert.Contains(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(12)), instructions);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 0);
    }

    [Fact]
    public void OrdinaryArgumentAndLocalRoundTripThroughWasmLocals()
    {
        var emitter = CreateEmitter();
        var context = CreateContext();
        var body = CreateBody();
        var stack = new List<CliValueKind>();
        var code = new RecordingInstructionWriter();

        Emit(emitter, Request(body, CilOperation.LoadArgument, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.StoreLocal, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.LoadLocal, 0, stack, context), code);
        Emit(emitter, Request(body, CilOperation.StoreArgument, 0, stack, context), code);

        Assert.Empty(stack);
        Assert.Equal(
            [
            WasmOpcodes.LocalGet, 0, WasmOpcodes.LocalSet, 2,
                WasmOpcodes.LocalGet, 2, WasmOpcodes.LocalSet, 1,
                WasmOpcodes.LocalGet, 1, WasmOpcodes.LocalSet, 2,
                WasmOpcodes.LocalGet, 2, WasmOpcodes.LocalSet, 0,
            ],
            code.ToArray());
    }

    [Fact]
    public void AddressTakenScalarWithoutSpillFailsWithMethodAndIlOffset()
    {
        var code = new RecordingInstructionWriter();
        var request = Request(
            CreateBody(),
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext());

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(CreateEmitter(), request, code));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("Test.Type::Run at IL_0000", exception.Message);
        Assert.Contains("address-taking scalar local", exception.Message);
    }

    [Fact]
    public void FilterCaptureUsesEnvironmentInsteadOfOriginalLocal()
    {
        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var code = new RecordingInstructionWriter();
        var request = Request(
            CreateBody(),
            CilOperation.LoadLocal,
            0,
            [],
            context);

        Emit(CreateEmitter(), request, code);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Equal(WasmOpcodes.LocalGet, code.ToArray()[0]);
        Assert.Equal(30, code.ToArray()[1]);
    }

    [Theory]
    [InlineData(CilOperation.LoadLocalAddress, false)]
    [InlineData(CilOperation.LoadArgumentAddress, true)]
    public void FilterCaptureAddressUsesTheEnvironment(
        CilOperation operation,
        bool isArgument)
    {
        var capture = new FilterCapture(
            new CapturedSlot(isArgument, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var request = Request(CreateBody(), operation, 0, [], context);

        Emit(CreateEmitter(), request, new RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
    }

    [Theory]
    [InlineData(CilOperation.StoreLocal, false)]
    [InlineData(CilOperation.StoreArgument, true)]
    public void FilterCaptureStoreUpdatesTheEnvironment(
        CilOperation operation,
        bool isArgument)
    {
        var capture = new FilterCapture(
            new CapturedSlot(isArgument, 0),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            4,
            []);
        var context = CreateContext() with
        {
            ValueFrame = 30,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var request = Request(
            CreateBody(),
            operation,
            0,
            [CliValueKind.I4],
            context);

        Emit(CreateEmitter(), request, new RecordingInstructionWriter());

        Assert.Empty(request.Stack);
    }

    [Fact]
    public void ValueTypeArgumentStoreCopiesTheValueBlock()
    {
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var request = Request(
            CreateBody(),
            CilOperation.StoreArgument,
            0,
            [CliValueKind.ValueType],
            CreateContext());
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Empty(request.Stack);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand.Alignment == WasmOpcodes.MemoryCopy);
    }

    [Fact]
    public void ScalarArgumentAddressWithoutSpillFailsWithMethodAndOffset()
    {
        var request = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());

        var exception = Assert.Throws<CompilerException>(() =>
            Emit(CreateEmitter(), request, new RecordingInstructionWriter()));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("address-taking a scalar argument", exception.Message);
    }

    [Fact]
    public void ValueArgumentsAndLocalsUseTheirValueFrames()
    {
        var valueType = CreateValueType();
        var body = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        var emitter = CreateEmitter(valueType);

        var loadArgument = Request(body, CilOperation.LoadArgument, 0, [], context);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadArgument.Stack);

        var storeArgument = Request(
            body,
            CilOperation.StoreArgument,
            0,
            [CliValueKind.ValueType],
            context);
        Emit(emitter, storeArgument, new RecordingInstructionWriter());
        Assert.Empty(storeArgument.Stack);

        var loadLocal = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, loadLocal, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadLocal.Stack);

        var storeLocal = Request(
            body,
            CilOperation.StoreLocal,
            0,
            [CliValueKind.ValueType],
            context);
        Emit(emitter, storeLocal, new RecordingInstructionWriter());
        Assert.Empty(storeLocal.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PromotedAggregateLanesStayCoherentAcrossWholeLocalTransfers(
        WasmTarget target)
    {
        var valueType = CreateValueType();
        var body = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var promotion = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                0,
                AddressTakenAggregateLocalPlan.CreateI4Storage(valueType, 12)),
            [],
            []);
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                ImmutableHashSet<int>.Empty.Add(0)),
            AddressTakenAggregatePromotions = new(
                promotion,
                ImmutableDictionary<int, ImmutableArray<int>>.Empty.Add(
                    0,
                    [40, 41, 42])),
        };
        var emitter = CreateEmitter(WasmTargetLayout.For(target), valueType);

        var load = Request(body, CilOperation.LoadLocal, 0, [], context);
        var loadCode = new RecordingInstructionWriter();
        Emit(emitter, load, loadCode);
        Assert.Equal([CliValueKind.ValueType], load.Stack);
        Assert.Equal(3, loadCode.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Store));

        var store = Request(
            body,
            CilOperation.StoreLocal,
            0,
            [CliValueKind.ValueType],
            context);
        var storeCode = new RecordingInstructionWriter();
        Emit(emitter, store, storeCode);
        Assert.Empty(store.Stack);
        Assert.Equal(3, storeCode.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Load));
    }

    [Theory]
    [InlineData(CilOperation.LoadArgument, 0)]
    [InlineData(CilOperation.LoadLocal, 1)]
    public void DirectReturnValueLoadsBorrowTheirExistingAddress(
        CilOperation operation,
        int sourceLocal)
    {
        var valueType = CreateValueType();
        var body = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                0,
                [],
                [],
                [],
                [])
            {
                DirectValueLoadOffsets = [0],
            },
        };
        var request = Request(body, operation, 0, [], context);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (uint)sourceLocal);
        Assert.DoesNotContain(code.ToInstructions(), instruction =>
            instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I32Load or
                WasmOpcodes.I64Store or WasmOpcodes.I32Store ||
            instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand.Alignment == WasmOpcodes.MemoryCopy);
    }

    [Fact]
    public void SpilledScalarLocalsAndArgumentsUseTheirFrameOffsets()
    {
        var body = CreateBody();
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty,
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                ImmutableDictionary<int, int>.Empty,
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        var emitter = CreateEmitter();

        var loadArgument = Request(body, CilOperation.LoadArgument, 0, [], context);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.I4], loadArgument.Stack);

        var storeArgument = Request(body, CilOperation.StoreArgument, 0, [CliValueKind.I4], context);
        Emit(emitter, storeArgument, new RecordingInstructionWriter());
        Assert.Empty(storeArgument.Stack);

        var loadLocal = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, loadLocal, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.I4], loadLocal.Stack);

        var storeLocal = Request(body, CilOperation.StoreLocal, 0, [CliValueKind.I4], context);
        Emit(emitter, storeLocal, new RecordingInstructionWriter());
        Assert.Empty(storeLocal.Stack);
    }

    [Fact]
    public void Memory64StoresCompatiblePointersFromTheirActualNativeIntegerLocal()
    {
        var declared = CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress);
        var body = CreateBody() with { LocalSignatureTypes = [declared] };
        var baseContext = CreateContext();
        var nativeLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            baseContext.StackLocals,
            0,
            CliValueKind.NativeInt,
            WasmTargetLayout.Wasm64);
        var emitter = CreateEmitter(WasmTargetLayout.Wasm64, declared);

        var spilledContext = baseContext with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                ImmutableDictionary<int, int>.Empty,
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                ImmutableDictionary<int, int>.Empty,
                ImmutableHashSet<int>.Empty.Add(0)),
        };
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreLocal, 0, [CliValueKind.NativeInt], spilledContext),
            nativeLocal);
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreArgument, 0, [CliValueKind.NativeInt], spilledContext),
            nativeLocal);

        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            declared,
            4,
            []);
        var captureContext = baseContext with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(capture.Slot, capture)),
        };
        AssertReadsLocal(
            emitter,
            Request(body, CilOperation.StoreLocal, 0, [CliValueKind.NativeInt], captureContext),
            nativeLocal);
    }

    [Fact]
    public void AddressTakingSupportsValueTypesAndSpilledScalars()
    {
        var valueType = CreateValueType();
        var valueBody = CreateBody() with
        {
            LocalSignatureTypes = [valueType],
        };
        var valueRequest = Request(
            valueBody,
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext());
        var emitter = CreateEmitter(valueType);

        Emit(emitter, valueRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], valueRequest.Stack);

        var spilledRequest = Request(
            CreateBody(),
            CilOperation.LoadLocalAddress,
            0,
            [],
            CreateContext() with
            {
                ValueLayout = new ValueFrameLayout(
                    8,
                    [],
                    [],
                    [],
                    ImmutableHashSet<int>.Empty.Add(0)),
            });

        Emit(CreateEmitter(), spilledRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], spilledRequest.Stack);
    }

    [Fact]
    public void PromotedScalarAddressUsesShadowWhileOrdinaryAccessUsesPrimaryLocal()
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type),
            []);
        var context = CreateContext() with
        {
            AddressTakenScalarPromotions = new(
                plan,
                ImmutableDictionary<int, int>.Empty.Add(0, 41)),
        };
        var body = CreateBody();
        var addressCode = new RecordingInstructionWriter();
        var addressRequest = Request(
            body,
            CilOperation.LoadLocalAddress,
            0,
            [],
            context);

        Emit(CreateEmitter(), addressRequest, addressCode);

        Assert.Equal([CliValueKind.ManagedAddress], addressRequest.Stack);
        Assert.Equal(WasmOpcodes.LocalGet, addressCode.ToArray()[0]);
        Assert.Equal(41, addressCode.ToArray()[1]);

        var loadCode = new RecordingInstructionWriter();
        var loadRequest = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(CreateEmitter(), loadRequest, loadCode);
        Assert.Equal(WasmOpcodes.LocalGet, loadCode.ToArray()[0]);
        Assert.Equal(context.LocalBase, loadCode.ToArray()[1]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BranchLocalScalarStorageUsesItsAddressBindingOnlyInsideTheFramedArm(WasmTarget target)
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var body = CreateBody() with { Locals = [CliValueKind.I8], LocalSignatureTypes = [type] };
        var unframed = CreateContext() with
        {
            AddressTakenScalarPromotions = new(new(ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type), []),
                ImmutableDictionary<int, int>.Empty.Add(0, 41)),
        };
        var framed = unframed with { ValueLayout = new(16, ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], [0]) };
        var emitter = CreateEmitter(WasmTargetLayout.For(target), type);
        foreach (var operation in new[] { CilOperation.LoadLocal, CilOperation.StoreLocal })
        {
            var code = new RecordingInstructionWriter();
            var stack = operation == CilOperation.LoadLocal ? new List<CliValueKind>() : [CliValueKind.I8];
            Emit(emitter, Request(body, operation, 0, stack, framed), code);
            var instructions = code.ToInstructions();
            Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(41)), instructions[0]);
            Assert.Contains(instructions, instruction => instruction.Opcode ==
                (operation == CilOperation.LoadLocal ? WasmOpcodes.I64Load : WasmOpcodes.I64Store));
            Assert.Equal(operation == CilOperation.LoadLocal ? new[] { CliValueKind.I8 } : [], stack);

            var registerCode = new RecordingInstructionWriter();
            var registerStack = operation == CilOperation.LoadLocal ? new List<CliValueKind>() : [CliValueKind.I8];
            Emit(emitter, Request(body, operation, 0, registerStack, unframed), registerCode);
            var registerInstructions = registerCode.ToInstructions();
            Assert.DoesNotContain(registerInstructions, instruction => instruction.Opcode is WasmOpcodes.I64Load or WasmOpcodes.I64Store);
            Assert.Contains(registerInstructions, instruction => instruction.Opcode ==
                (operation == CilOperation.LoadLocal ? WasmOpcodes.LocalGet : WasmOpcodes.LocalSet) &&
                instruction.Operand.UnsignedValue == (uint)unframed.LocalBase);
        }
    }

    [Fact]
    public void ArgumentAddressSupportsSpillsAndMethodInstanceReturnWidth()
    {
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                8,
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                [],
                []),
        };
        var offsetRequest = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            context);
        Emit(CreateEmitter(), offsetRequest, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], offsetRequest.Stack);

        var valueType = CreateValueType();
        var method = new FakeProgram().GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            method,
            valueType,
            [],
            MethodSignatureModel.Create(CliValueKind.ValueType, CliValueKind.I4));
        var methodInstanceRequest = Request(
            CreateBody() with { MethodInstance = instance },
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());
        Emit(CreateEmitter(valueType),
            methodInstanceRequest,
            new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], methodInstanceRequest.Stack);

        var managedAddressRequest = Request(
            CreateBody(),
            CilOperation.LoadArgumentAddress,
            0,
            [],
            CreateContext());
        Emit(CreateEmitter(CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress)),
            managedAddressRequest,
            new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ManagedAddress], managedAddressRequest.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ValueFilterCapturesLoadAndStoreWithoutScalarMemoryConversions(
        WasmTarget target)
    {
        var valueType = CreateValueType();
        var capture = new FilterCapture(
            new CapturedSlot(false, 0),
            valueType,
            4,
            []);
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(24, [], [],
                ImmutableDictionary<int, int>.Empty.Add(0, 16), []),
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture)),
        };
        var body = CreateBody() with { LocalSignatureTypes = [valueType] };
        var emitter = CreateEmitter(WasmTargetLayout.For(target), valueType);

        var load = Request(body, CilOperation.LoadLocal, 0, [], context);
        Emit(emitter, load, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], load.Stack);

        var store = Request(body, CilOperation.StoreLocal, 0, [CliValueKind.ValueType], context);
        var storeCode = new RecordingInstructionWriter();
        Emit(emitter, store, storeCode);
        Assert.Empty(store.Stack);
        var sizeConstant = target == WasmTarget.Wasm32
            ? WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(4))
            : WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(4));
        Assert.Contains(sizeConstant, storeCode.ToInstructions());

        var argumentCapture = capture with { Slot = new CapturedSlot(true, 0) };
        var argumentContext = CreateContext() with
        {
            ValueLayout = context.ValueLayout,
            FilterEnvironment = new FilterEnvironmentLayout(
                8,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    argumentCapture.Slot,
                    argumentCapture)),
        };
        var loadArgument = Request(
            body,
            CilOperation.LoadArgument,
            0,
            [],
            argumentContext);
        Emit(emitter, loadArgument, new RecordingInstructionWriter());
        Assert.Equal([CliValueKind.ValueType], loadArgument.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadArgument, false)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadArgument, true)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadLocal, false)]
    [InlineData(WasmTarget.Wasm32, CilOperation.LoadLocal, true)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadArgument, false)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadArgument, true)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadLocal, false)]
    [InlineData(WasmTarget.Wasm64, CilOperation.LoadLocal, true)]
    public void CapturedAggregateLoadsCopyUnlessImmediatelyReturned(
        WasmTarget target, CilOperation operation, bool directReturn)
    {
        var value = CreateValueType();
        var capture = new FilterCapture(new(operation == CilOperation.LoadArgument, 0),
            value, 4, []);
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(32, [], [],
                ImmutableDictionary<int, int>.Empty.Add(0, 16), [])
            {
                DirectValueLoadOffsets = directReturn ? [0] : [],
            },
            FilterEnvironment = new FilterEnvironmentLayout(8, 0, 0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(capture.Slot, capture)),
        };
        var body = CreateBody() with { LocalSignatureTypes = [value] };
        var code = new RecordingInstructionWriter();
        var request = Request(body, operation, 0, [], context);

        Emit(CreateEmitter(WasmTargetLayout.For(target), value), request, code);

        Assert.Equal([CliValueKind.ValueType], request.Stack);
        var instructions = code.ToInstructions();
        var copy = WasmInstruction.WithOperand(WasmOpcodes.Prefixed,
            WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0));
        Assert.Equal(directReturn ? 0 : 1, instructions.Count(instruction => instruction == copy));
        var offset = directReturn ? capture.Offset : 16;
        var constant = target == WasmTarget.Wasm32
            ? WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(offset))
            : WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(offset));
        Assert.Contains(constant, instructions);
        if (!directReturn)
        {
            Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary)), instructions[^2]);
        }
    }

    [Fact]
    public void Memory64ValueFramesAndAddressConstantsUseI64Instructions()
    {
        var valueType = CreateValueType();
        var emitter = CreateEmitter(WasmTargetLayout.Wasm64, valueType);
        var body = CreateBody();
        var context = CreateContext() with
        {
            ValueLayout = new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                []),
        };
        var request = Request(body, CilOperation.LoadArgument, 0, [], context);

        Emit(emitter, request, new RecordingInstructionWriter());

        Assert.Equal([CliValueKind.ValueType], request.Stack);
    }

    [Fact]
    public void FrameAddressContractsValidateInputsAndSupportZeroOffsets()
    {
        IValueFrameAddressEmitter offsets =
            CreateValueFrameAddresses(new RecordingLayoutProvider(WasmTargetLayout.Wasm64));
        IValueFrameAddressEmitter captures =
            CreateValueFrameAddresses(new RecordingLayoutProvider());
        var context = CreateContext();
        var code = new RecordingInstructionWriter();

        offsets.Emit(code, context, 0);
        Assert.Throws<ArgumentNullException>(() => offsets.Emit(null!, context, 0));
        Assert.Throws<ArgumentNullException>(() => offsets.Emit(code, null!, 0));
        Assert.Throws<ArgumentNullException>(() => captures.Emit(code, context, null!));
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal, 0, 1)]
    [InlineData(CilOperation.StoreLocal, 1, 0)]
    public void PromotedAggregateLocalTransfersEmitNoMemoryWork(
        CilOperation operation,
        int inputCount,
        int expectedCount)
    {
        var valueType = CreateValueType();
        var body = new CilMethodBody(
            new FakeProgram().GetMethod(EntryKey),
            1,
            [CliValueKind.ValueType],
            [])
        {
            LocalSignatureTypes = [valueType],
        };
        var plan = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, valueType),
            [],
            [0],
            [0]);
        var context = CreateContext() with
        {
            TransientAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, TransientAggregateScalarLocals>.Empty.Add(
                    0,
                    new(40, 41))),
        };
        var request = Request(
            body,
            operation,
            0,
            Enumerable.Repeat(CliValueKind.ValueType, inputCount).ToList(),
            context);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Equal(expectedCount, request.Stack.Count);
        Assert.Empty(code.ToInstructions());
    }

    [Fact]
    public void PromotedAggregateLocalAddressProjectionEmitsNoMemoryWork()
    {
        var valueType = CreateValueType();
        var body = new CilMethodBody(
            new FakeProgram().GetMethod(EntryKey),
            1,
            [CliValueKind.ValueType],
            [])
        {
            LocalSignatureTypes = [valueType],
        };
        var plan = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, valueType),
            [],
            [0],
            [0]);
        var context = CreateContext() with
        {
            TransientAggregatePromotions = new(
                plan,
                ImmutableDictionary<int, TransientAggregateScalarLocals>.Empty.Add(
                    0,
                    new(40, 41))),
        };
        var request = Request(
            body,
            CilOperation.LoadLocalAddress,
            0,
            [],
            context);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(valueType), request, code);

        Assert.Equal([CliValueKind.ManagedAddress], request.Stack);
        Assert.Empty(code.ToInstructions());
    }

    private static IInstructionCommandProvider CreateEmitter()
        => CreateEmitter(WasmTargetLayout.Wasm32);

    private static IInstructionCommandProvider CreateEmitter(WasmTargetLayout target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target);
        var argumentTypes = CreateArgumentTypes(program);
        var argumentSignatureTypes = CreateArgumentSignatureTypes(program);
        return AsProvider(new LocalsArgumentsEmitter(
            program,
            layouts,
            layouts,
            argumentTypes,
            argumentSignatureTypes,
            CreateValueFrameAddresses(layouts),
            CreateFilterEnvironmentRoots(layouts),
            new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts)),
            new AddressTakenAggregateSynchronizationEmitter(layouts),
            new NetWasm.Compiler.Wasm.Emission.Arrays.ArrayMetadataSnapshotEmitter(
                layouts, layouts, new RectangularArrayLayoutProvider(layouts, layouts), CreateAddressInstructions(layouts),
                new ImplicitExceptionEmitter(layouts))));
    }

    private static IInstructionCommandProvider CreateEmitter(CliTypeIdentity argumentType)
        => CreateEmitter(WasmTargetLayout.Wasm32, argumentType);

    private static IInstructionCommandProvider CreateEmitter(
        WasmTargetLayout target,
        CliTypeIdentity argumentType)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(target);
        var resolver = new FixedArgumentTypeResolver(argumentType);
        return AsProvider(new LocalsArgumentsEmitter(
            program,
            layouts,
            layouts,
            resolver,
            resolver,
            CreateValueFrameAddresses(layouts),
            CreateFilterEnvironmentRoots(layouts),
            new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts)),
            new AddressTakenAggregateSynchronizationEmitter(layouts),
            new NetWasm.Compiler.Wasm.Emission.Arrays.ArrayMetadataSnapshotEmitter(
                layouts, layouts, new RectangularArrayLayoutProvider(layouts, layouts), CreateAddressInstructions(layouts),
                new ImplicitExceptionEmitter(layouts))));
    }

    private static CliTypeIdentity CreateValueType() => CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Value",
        isValueType: true,
        CliValueKind.ValueType);

    private static void Emit(
        IInstructionCommandProvider provider,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var command = provider.Commands.Single(candidate =>
            candidate.Operation == request.Instruction.Operation);
        command.Emit(request, code, CreateFunctionIndexResolver());
    }

    private static IInstructionCommandProvider AsProvider(
        IInstructionCommandProvider provider) => provider;

    private static void AssertReadsLocal(
        IInstructionCommandProvider emitter,
        InstructionEmissionRequest request,
        int local)
    {
        var code = new RecordingInstructionWriter();

        Emit(emitter, request, code);

        Assert.Contains(code.ToInstructions(), instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == (uint)local);
    }

    private static CilMethodBody CreateBody()
    {
        var program = new FakeProgram();
        return new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [CliValueKind.I4],
            [])
        {
            LocalSignatureTypes =
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
        };
    }

    private static InstructionEmissionRequest Request(
        CilMethodBody body,
        CilOperation operation,
        int index,
        List<CliValueKind> stack,
        MethodEmissionContext context) => new(
        Header(body),
        I(0, operation, new CilOperand.Index(index)),
        stack,
        context,
        CreateInstructionModuleTarget());

    private static MethodEmissionContext CreateContext()
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        return new MethodEmissionContext(
            roots,
            1,
            2,
            WasmLocalLayoutPlanner.CreateEvaluationStack(2, 1),
            8,
            9,
            10,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            11,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            12,
            FilterEnvironmentLayout.Empty,
            0,
            13,
            14,
            15,
            16,
            17,
            18)
        {
            ManagedThrowFunction = OptionalFunctionIndex.At(7),
        };
    }

    private sealed class FixedArgumentTypeResolver(CliTypeIdentity type) :
        IArgumentTypeResolver,
        IArgumentSignatureTypeResolver
    {
        public CliValueKind Resolve(StructuredMethodHeader header, int index) =>
            type.StackKind;

        CliTypeIdentity IArgumentSignatureTypeResolver.Resolve(
            StructuredMethodHeader header,
            int index) => type;
    }
}
