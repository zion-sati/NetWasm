using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class FilterEnvironmentRootEmitterTests
{
    [Fact]
    public void PublishesCapturedRootsInArgumentLocalAndSlotOrder()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var frames = new RecordingValueFrameAddressEmitter();
        var addresses = new RecordingAddressInstructionEmitter();
        var emitter = CreateEmitter(layouts, frames, addresses);
        var argument = new FilterCapture(new(true, 2),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 24,
            [(0, 1), (8, 0)]);
        var local = new FilterCapture(new(false, 1),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference), 40,
            [(16, 2)]);
        var environment = new FilterEnvironmentLayout(64, 3, 0,
            ImmutableDictionary<CapturedSlot, FilterCapture>.Empty
                .Add(local.Slot, local)
                .Add(argument.Slot, argument));
        var code = new EmitterTestSupport.RecordingInstructionWriter();

        emitter.Emit(code, CreateContext(environment));

        Assert.Equal([24, 24, 40], frames.Offsets);
        Assert.Equal([layouts.Target.ObjectReferenceSize,
            layouts.Target.ObjectReferenceSize * 2], addresses.Constants);
        Assert.Equal([AddressOperation.Add, AddressOperation.Add],
            addresses.Operations);
        Assert.NotEmpty(code.ToArray());
    }

    [Fact]
    public void EmptyEnvironmentWritesNothingAndMissingInputsAreRejected()
    {
        var emitter = CreateEmitter(new RecordingLayoutProvider(),
            new RecordingValueFrameAddressEmitter(),
            new RecordingAddressInstructionEmitter());
        var code = new EmitterTestSupport.RecordingInstructionWriter();
        var context = CreateContext(FilterEnvironmentLayout.Empty);

        emitter.Emit(code, context);

        Assert.Empty(code.ToArray());
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, context));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, null!));
    }

    private static IFilterEnvironmentRootEmitter CreateEmitter(
        ITargetLayout layouts,
        IValueFrameAddressEmitter frames,
        IAddressInstructionEmitter addresses) => new[]
    {
        new FilterEnvironmentRootEmitter(layouts, frames, addresses),
    }.Cast<IFilterEnvironmentRootEmitter>().Single();

    private static MethodEmissionContext CreateContext(
        FilterEnvironmentLayout environment) => new(
        new(EmitterTestSupport.EntryKey, [], []),
        1,
        2,
        WasmLocalLayoutPlanner.CreateEvaluationStack(2, 1),
        8,
        9,
        10,
        ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
        ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
        11,
        new(0, [], [], [], []),
        12,
        environment,
        0,
        13,
        14,
        15,
        16,
        17,
        18);

    private sealed class RecordingValueFrameAddressEmitter : IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context,
            int offset) => Offsets.Add(offset);

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            FilterCapture capture) => Offsets.Add(capture.Offset);
    }

    private sealed class RecordingAddressInstructionEmitter : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) =>
            Constants.Add(constant);

        public void Emit(IWasmInstructionWriter code, AddressOperation operation) =>
            Operations.Add(operation);
    }
}
