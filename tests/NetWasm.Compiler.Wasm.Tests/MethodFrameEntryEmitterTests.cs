using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodFrameEntryEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SelectedAggregateArgumentInitializesLanesWithoutCopyOrRebinding(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var copies = new RecordingValueCopies();
        var value = CliTypeIdentity.Named(Assembly, "Test", "SelectedArgument", true);
        var storage = new ManagedAggregateStorageId(
            ManagedAggregateStorageKind.Argument,
            0);
        var low = new ManagedAggregateSsaLane(storage, 0);
        var high = new ManagedAggregateSsaLane(storage, sizeof(int));
        var plan = new ManagedAggregateSsaPlan(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>
                .Empty.Add(storage, new(storage, value, 2 * sizeof(int), true)),
            [low, high],
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
                .Empty.Add(low, new(0)).Add(high, new(1)),
            [], [], [], [], [], []);
        var context = CreateMethodEmissionContext() with
        {
            ParameterOffset = 1,
            ValueLayout = new(
                2 * sizeof(int),
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0),
                [],
                []),
            ManagedAggregateValues = new(
                plan,
                ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty
                    .Add(low, 30)
                    .Add(high, 31)),
        };
        var emitter = new MethodFrameEntryEmitter(
            layouts,
            new FixedArgumentValueLayout(new(value, 2 * sizeof(int), sizeof(int), [])),
            imports,
            new StackTraceFrameEntryEmitter(imports),
            addresses,
            new ValueFrameEntryEmitter(layouts,
                new FixedArgumentValueLayout(new(value, 2 * sizeof(int), sizeof(int), [])),
                new RecordingArgumentSignatureTypeResolver(value), imports, addresses,
                new ValueFrameInitializationEmitter(layouts, layouts, addresses), copies,
                new ManagedAggregateValueInitializer(layouts)),
            new RecordingFilterEnvironmentRootEmitter(),
            new ManagedByRefAggregateInitializationEmitter(layouts));
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            code,
            new StructuredMethodHeader(
                new FakeProgram().GetMethod(EntryKey), null, 1, [], [], []),
            context);

        Assert.Empty(copies.Requests);
        var instructions = code.ToInstructions();
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 30);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 31);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalSet &&
            instruction.Operand.UnsignedValue == 1);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 12, 4)]
    [InlineData(WasmTarget.Wasm32, 16, 8)]
    [InlineData(WasmTarget.Wasm64, 16, 8)]
    [InlineData(WasmTarget.Wasm64, 24, 8)]
    public void CopiesMutableValueArgumentsAndRebindsEachPointerBeforeFilterCapture(
        WasmTarget target, int size, int alignment)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var copies = new RecordingValueCopies();
        var value = CliTypeIdentity.Named(Assembly, "Test", "OwnedArgument", true);
        var values = new FixedArgumentValueLayout(new(value, size, alignment, [0]));
        var capture = new FilterCapture(new(true, 0), value, 2 * size + 8, []);
        var context = CreateMethodEmissionContext() with
        {
            ParameterOffset = 1,
            ValueLayout = new ValueFrameLayout(3 * size + 8, [],
                ImmutableDictionary<int, int>.Empty.Add(0, 0).Add(1, size), [], []),
            FilterEnvironment = new FilterEnvironmentLayout(3 * size + 8, 0, 2 * size,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(capture.Slot, capture))
            {
                ParentRootFrameOffset = 2 * size + 4,
            },
        };
        var emitter = new[] { new MethodFrameEntryEmitter(layouts, values, imports,
            new StackTraceFrameEntryEmitter(imports), addresses,
            new ValueFrameEntryEmitter(layouts, values, new RecordingArgumentSignatureTypeResolver(value),
                imports, addresses, new ValueFrameInitializationEmitter(layouts, values, addresses), copies,
                new ManagedAggregateValueInitializer(layouts)),
            new RecordingFilterEnvironmentRootEmitter(),
            new ManagedByRefAggregateInitializationEmitter(layouts)) }
            .Cast<IMethodFrameEntryEmitter>().Single();
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, new StructuredMethodHeader(new FakeProgram().GetMethod(EntryKey),
            null, 1, [], [], []), context);

        Assert.Equal(2, copies.Requests.Count);
        Assert.All(copies.Requests, request =>
        {
            Assert.Equal(context.ObjectTemporary, request.Destination);
            Assert.Equal(size, request.Size);
            Assert.Equal(alignment, request.Alignment);
        });
        Assert.Equal([1, 2], copies.Requests.Select(request => request.Source).Order());
        var instructions = code.ToInstructions();
        var rebinding = instructions.Select((instruction, index) => (instruction, index))
            .Where(item => item.instruction.Opcode == WasmOpcodes.LocalSet &&
                item.instruction.Operand.UnsignedValue is 1 or 2).ToArray();
        Assert.Equal(2, rebinding.Length);
        Assert.All(rebinding, item => Assert.Equal(
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary)),
            instructions[item.index - 1]));
        var captureCopy = Array.FindIndex(instructions.ToArray(), instruction =>
            instruction.Opcode == WasmOpcodes.Prefixed &&
            instruction.Operand == WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0));
        Assert.True(captureCopy > rebinding.Max(item => item.index));
        Assert.Equal([0, size, 2 * size, 2 * size + 4, capture.Offset], addresses.Offsets.Order().ToArray());
    }

    private sealed class FixedArgumentValueLayout(ValueLayout layout) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            Assert.Equal(layout.Type, type);
            return layout;
        }
    }

    private sealed class RecordingValueCopies : IValueCopyEmitter
    {
        public List<(int Destination, int Size, int Alignment, int Source)> Requests { get; } = [];

        public void Emit(IWasmInstructionWriter code, int destinationLocal, int size,
            int alignment, Action<IWasmInstructionWriter> emitSource, MethodEmissionContext? inlineContext = null)
        {
            var source = new RecordingInstructionWriter();
            emitSource(source);
            var instruction = Assert.Single(source.ToInstructions());
            Assert.Equal(WasmOpcodes.LocalGet, instruction.Opcode);
            Requests.Add((destinationLocal, size, alignment, (int)instruction.Operand.UnsignedValue));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClearsMultipleExceptionRootsWithOneMemoryFill(
        WasmTarget target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var first = new RootSource(RootSourceKind.Argument, 0);
        var second = new RootSource(RootSourceKind.Local, 0);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new MethodRootMap(
                EntryKey,
                ImmutableDictionary<RootSource, int>.Empty
                    .Add(first, 0)
                    .Add(second, 1),
                []),
            ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty
                .Add(new(0), 2)
                .Add(new(1), 3),
        };
        var code = new RecordingInstructionWriter();

        CreateMethodFrameEntry(
            program,
            layouts,
            WasmRuntimeImports.CreateCatalog()).Emit(
                code,
                new StructuredMethodHeader(
                    program.GetMethod(EntryKey), null, 1, [], [], []),
                context);

        var instructions = code.ToInstructions();
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue ==
                (uint)WasmRuntimeImports.CreateCatalog().Resolve(
                    RuntimeImportSymbol.RootFrameEnter));
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.LocalTee &&
                instruction.Operand.UnsignedValue == (uint)context.RootFrame);
        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand.UnsignedValue == (uint)context.RootFrame);
        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Opcode is WasmOpcodes.I32Store or
                WasmOpcodes.I64Store);
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
                instruction.Operand == WasmInstructionOperand.PrefixedPair(
                    WasmOpcodes.MemoryFill,
                    0));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClearsSingleExceptionRootWithOneStore(WasmTarget target)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new MethodRootMap(
                EntryKey,
                ImmutableDictionary<RootSource, int>.Empty.Add(
                    new RootSource(RootSourceKind.Argument, 0),
                    0),
                []),
            ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty
                .Add(new(0), 1),
        };
        var code = new RecordingInstructionWriter();

        CreateMethodFrameEntry(
            program,
            layouts,
            WasmRuntimeImports.CreateCatalog()).Emit(
                code,
                new StructuredMethodHeader(
                    program.GetMethod(EntryKey), null, 1, [], [], []),
                context);

        var instructions = code.ToInstructions();
        var store = Assert.Single(instructions.Where(instruction =>
            instruction.Opcode == (target == WasmTarget.Wasm64
                ? WasmOpcodes.I64Store
                : WasmOpcodes.I32Store)));
        Assert.Equal((uint)layouts.Target.ObjectReferenceSize, store.Operand.Offset);
        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Prefixed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionRootsComposeWithFilterAndStackTraceFrames(bool includeFilterAndTrace)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var context = CreateMethodEmissionContext() with
        {
            ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(new(0), 0),
            ValueLayout = new ValueFrameLayout(includeFilterAndTrace ? 16 : 0, [], [], [], []),
            FilterEnvironment = includeFilterAndTrace
                ? new FilterEnvironmentLayout(16, 1, 0, [])
                : FilterEnvironmentLayout.Empty,
            StackTraceMethodId = includeFilterAndTrace ? 1 : 0,
            RuntimeImportSelection = new(WasmModuleProfile.CoreApplication,
                IncludeTerminalExceptionReporter: true, IncludeStackTrace: true),
        };
        var code = new RecordingInstructionWriter();
        CreateMethodFrameEntry(program, layouts, imports).Emit(code,
            new StructuredMethodHeader(program.GetMethod(EntryKey), null, 1, [], [], []), context);
        CreateMethodFrameExit(imports).Emit(code, context);
        var instructions = code.ToInstructions();
        Assert.Equal(0, context.RootMap.SlotCount);
        Assert.Equal(1, context.RootSlotCount);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        Assert.Equal(includeFilterAndTrace ? 2 : 1, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter)));
        Assert.Equal(includeFilterAndTrace ? 2 : 1, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)imports.Resolve(RuntimeImportSymbol.RootFrameLeave)));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm32, 1)]
    [InlineData(WasmTarget.Wasm64, 1)]
    public void InitializesValueTypeFilterCaptureForEachTargetWidth(
        WasmTarget target,
        int rootSlots)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true,
            CliValueKind.ValueType);
        var capture = new FilterCapture(
            new CapturedSlot(true, 0),
            valueType,
            8,
            []);
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = new ValueFrameLayout(24, [], [], [], []),
            FilterEnvironment = new FilterEnvironmentLayout(
                24,
                rootSlots,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture))
            {
                ParentRootFrameOffset = 4,
            },
        };
        var body = new CilMethodBody(
            program.GetMethod(EntryKey),
            1,
            [],
            []);
        var code = new RecordingInstructionWriter();

        CreateMethodFrameEntry(
            program,
            layouts,
            WasmRuntimeImports.CreateCatalog()).Emit(code, Header(body), context);

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.Prefixed, bytes);
        Assert.Contains(
            target == WasmTarget.Wasm64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            bytes);
    }

    [Fact]
    public void InitializesRootFrameValueFrameAndValueArgumentStorage()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var argumentTypes = new RecordingArgumentSignatureTypeResolver(
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new MethodRootMap(
                EntryKey,
                ImmutableDictionary<RootSource, int>.Empty.Add(
                    new RootSource(RootSourceKind.Argument, 0),
                    0),
                []),
            ValueLayout = new ValueFrameLayout(
                24,
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                ImmutableDictionary<int, int>.Empty.Add(0, 8),
                [],
                []),
        };
        var emitter = CreateEmitter(
            layouts,
            imports,
            argumentTypes,
            addresses,
            filterRoots);
        var code = new RecordingInstructionWriter();

        emitter.Emit(
            code,
            new StructuredMethodHeader(
                program.GetMethod(EntryKey),
                null,
                1,
                [CliValueKind.I4],
                [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
                []),
            context);

        var instructions = code.ToInstructions();
        Assert.Equal(
            context.RootSlotCount,
            instructions.First(instruction =>
                instruction.Opcode == WasmOpcodes.I32Constant).Operand.SignedValue);
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue ==
                (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand.UnsignedValue == (uint)context.RootFrame);
        Assert.DoesNotContain(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.LocalTee &&
                instruction.Operand.UnsignedValue == (uint)context.RootFrame);
        Assert.Contains(
            instructions,
            instruction => instruction.Opcode == WasmOpcodes.Call &&
                instruction.Operand.UnsignedValue ==
                (uint)imports.Resolve(RuntimeImportSymbol.ValueFrameEnter));
        Assert.Equal([4, 4, 8], addresses.Offsets);
        Assert.Equal(0, filterRoots.Calls);
        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
        Assert.DoesNotContain(
            code.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.Prefixed &&
                instruction.Operand == WasmInstructionOperand.PrefixedPair(
                    WasmOpcodes.MemoryFill,
                    0));
    }

    [Fact]
    public void BindsPromotedScalarFrameOffsetToItsShadowLocal()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty.Add(0, type),
            []);
        var context = CreateMethodEmissionContext() with
        {
            LocalBase = 7,
            ValueLayout = new ValueFrameLayout(
                8,
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                [],
                [],
                [])
            {
                AddressTakenScalarPromotions = plan,
            },
            AddressTakenScalarPromotions = new(
                plan,
                ImmutableDictionary<int, int>.Empty.Add(0, 41)),
        };
        var code = new RecordingInstructionWriter();

        CreateEmitter(
            layouts,
            imports,
            new RecordingArgumentSignatureTypeResolver(type),
            addresses,
            new RecordingFilterEnvironmentRootEmitter()).Emit(
                code,
                new StructuredMethodHeader(
                    program.GetMethod(EntryKey),
                    null,
                    1,
                    [CliValueKind.I4],
                    [type],
                    []),
                context);

        Assert.Contains(
            code.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand.UnsignedValue == 41u);
        Assert.DoesNotContain(
            code.ToInstructions(),
            instruction => instruction.Opcode == WasmOpcodes.LocalSet &&
                instruction.Operand.UnsignedValue == 7u);
        Assert.Contains(4, addresses.Offsets);
    }

    [Fact]
    public void StoresScalarFilterArgumentCaptureAndPublishesFilterRoot()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var addresses = new RecordingValueFrameAddressEmitter();
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var capture = new FilterCapture(
            new CapturedSlot(true, 1),
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            12,
            []);
        var context = CreateMethodEmissionContext() with
        {
            FilterEnvironment = new FilterEnvironmentLayout(
                24,
                0,
                0,
                ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(
                    capture.Slot,
                    capture))
            {
                ParentRootFrameOffset = 4,
            },
        };
        var emitter = CreateEmitter(
            layouts,
            imports,
            new RecordingArgumentSignatureTypeResolver(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            addresses,
            filterRoots);

        var code = new RecordingInstructionWriter();
        emitter.Emit(
            code,
            new StructuredMethodHeader(
                new FakeProgram().GetMethod(EntryKey), null, 1, [], [], []),
            context);

        Assert.Equal([0, 4, 12], addresses.Offsets);
        Assert.Equal(1, filterRoots.Calls);
        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
        Assert.DoesNotContain(WasmOpcodes.Prefixed, code.ToArray());
    }

    private static IMethodFrameEntryEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IRuntimeImportResolver imports,
        IArgumentSignatureTypeResolver argumentTypes,
        IValueFrameAddressEmitter addresses,
        IFilterEnvironmentRootEmitter filterRoots) =>
        new[]
        {
            new MethodFrameEntryEmitter(
                layouts,
                layouts,
                imports,
                new StackTraceFrameEntryEmitter(imports),
                addresses,
                new ValueFrameEntryEmitter(layouts, layouts, argumentTypes, imports, addresses,
                    new ValueFrameInitializationEmitter(layouts, layouts, addresses),
                    new SmallValueCopyEmitter(new BulkValueCopyEmitter(layouts)),
                    new ManagedAggregateValueInitializer(layouts)),
                filterRoots,
                new ManagedByRefAggregateInitializationEmitter(layouts)),
        }.Cast<IMethodFrameEntryEmitter>().Single();

    private sealed class RecordingValueFrameAddressEmitter : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, int offset) =>
            Offsets.Add(offset);

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            FilterCapture capture) => Offsets.Add(capture.Offset);
    }

    private sealed class RecordingFilterEnvironmentRootEmitter :
        IFilterEnvironmentRootEmitter
    {
        public int Calls { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) => Calls++;
    }

    private sealed class RecordingArgumentSignatureTypeResolver(CliTypeIdentity type) :
        IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) => type;
    }
}
