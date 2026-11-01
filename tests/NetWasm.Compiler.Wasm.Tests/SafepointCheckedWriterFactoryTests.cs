using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

// A call to an intrinsic publishes the caller's roots only when the safepoint
// classifier names the intrinsic's method, so an intrinsic it does not name
// may not call the runtime where it can collect: what the caller holds would
// be freed and its memory reused.
public sealed class SafepointCheckedWriterFactoryTests
{
    private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");
    private static readonly RuntimeImportCatalog Imports = WasmRuntimeImports.CreateCatalog();

    // A component's core module with every optional import has each collecting
    // function.
    private static readonly RuntimeImportSelection Everything = new(WasmModuleProfile.ComponentCoreModule, true,
        IncludeStackTrace: true, IncludeExceptionCapture: true, IncludeEphemeronHandles: true,
        IncludeFinalization: true, IncludeWeakHandles: true, IncludeRandomSeed: true);

    public static TheoryData<string> CollectingSymbols() =>
        [.. CollectingRuntimeImports.Symbols.Select(static symbol => symbol.ToString()).Order()];

    [Theory]
    [MemberData(nameof(CollectingSymbols))]
    public void AnIntrinsicThatIsNoSafepointCannotCallTheRuntimeWhereItCollects(string symbol)
    {
        var emitter = Checked(new CallingEmitter(Enum.Parse<RuntimeImportSymbol>(symbol)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(InModule(Request("System", "Unlisted", "Run"), Everything), new EmitterTestSupport.RecordingInstructionWriter()));

        Assert.Contains(symbol, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Unlisted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIntrinsicThatIsNoSafepointMayCallTheRuntimeWhereItCannotCollect()
    {
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        Checked(new CallingEmitter(RuntimeImportSymbol.IsAssignable)).Emit(InModule(Request("System", "Unlisted", "Run"), Everything), writer);

        AssertCalls(writer, RuntimeImportSymbol.IsAssignable);
    }

    [Fact]
    public void ASafepointIntrinsicMayCollect()
    {
        var writer = new EmitterTestSupport.RecordingInstructionWriter();

        Checked(new CallingEmitter(RuntimeImportSymbol.Allocate)).Emit(InModule(Request("System", "DelegateRuntime", "Clone"), Everything), writer);

        AssertCalls(writer, RuntimeImportSymbol.Allocate);
    }

    // Leaving imports out of a module renumbers the ones after them, so the
    // index the full catalog gives a collecting function names another function
    // in a leaner module, while the lean index names the collecting one.
    [Fact]
    public void TheCheckReadsTheIndicesOfTheModuleTheIntrinsicEmitsInto()
    {
        var full = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, true, IncludeStackTrace: true,
            IncludeExceptionCapture: true, IncludeEphemeronHandles: true, IncludeFinalization: true,
            IncludeWeakHandles: true, IncludeRandomSeed: true);
        var lean = full with { IncludeStackTrace = false, IncludeExceptionCapture = false, IncludeEphemeronHandles = false };
        var fullIndex = (uint)Imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint, full);
        var leanIndex = (uint)Imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint, lean);
        Assert.NotEqual(fullIndex, leanIndex);
        Assert.DoesNotContain(fullIndex, CollectingRuntimeImports.Symbols
            .Where(lean.Includes)
            .Select(symbol => (uint)Imports.Resolve(symbol, lean)));
        var request = InModule(Request("System", "Unlisted", "Run"), lean);

        Checked(new IndexCallingEmitter(fullIndex)).Emit(request, new EmitterTestSupport.RecordingInstructionWriter());
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Checked(new IndexCallingEmitter(leanIndex)).Emit(request, new EmitterTestSupport.RecordingInstructionWriter()));

        Assert.Contains(nameof(RuntimeImportSymbol.FinalizerSafepoint), exception.Message, StringComparison.Ordinal);
    }

    // A module without finalization has no finalizer functions to call.
    [Fact]
    public void AModuleWithoutAnImportHasNoIndexForIt()
    {
        var selection = new RuntimeImportSelection(WasmModuleProfile.CoreApplication, true, IncludeFinalization: false);

        var indices = new CollectingRuntimeImports(Imports).Resolve(selection);

        Assert.DoesNotContain(RuntimeImportSymbol.FinalizerSafepoint, indices.Values);
        Assert.DoesNotContain(RuntimeImportSymbol.AllocateFinalizable, indices.Values);
        Assert.Equal((uint)Imports.Resolve(RuntimeImportSymbol.Allocate, selection),
            Assert.Single(indices, index => index.Value == RuntimeImportSymbol.Allocate).Key);
    }

    private static RuntimeIntrinsicEmissionRequest InModule(RuntimeIntrinsicEmissionRequest request, RuntimeImportSelection selection) =>
        request with
        {
            Call = request.Call with
            {
                Instruction = request.Instruction with
                {
                    Target = request.Instruction.Target with { RuntimeImportSelection = selection },
                },
            },
        };

    // A safepoint intrinsic writes straight to the caller's writer.
    [Fact]
    public void ASafepointIntrinsicKeepsTheCallersWriter()
    {
        var writer = new EmitterTestSupport.RecordingInstructionWriter();
        var factory = new SafepointCheckedWriterFactory(new RuntimeAllocationSafepointClassifier(), new CollectingRuntimeImports(Imports));

        Assert.Same(writer, ((ISafepointCheckedWriterFactory)factory).Create(writer, Request("System", "DelegateRuntime", "Clone")));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, Request("System", "DelegateRuntime", "Clone")));
        Assert.Throws<ArgumentNullException>(() => factory.Create(writer, null!));
    }

    private static IRuntimeIntrinsicEmitter Checked(IRuntimeIntrinsicEmitter emitter) => Checked(emitter, Imports);

    // The emitter writing through the factory's writer, as the intrinsic call emitter does.
    internal static IRuntimeIntrinsicEmitter Checked(IRuntimeIntrinsicEmitter emitter, IRuntimeImportResolver imports) =>
        new CheckedEmitter(emitter, new SafepointCheckedWriterFactory(
            new RuntimeAllocationSafepointClassifier(), new CollectingRuntimeImports(imports)));

    private sealed class CheckedEmitter(IRuntimeIntrinsicEmitter emitter, ISafepointCheckedWriterFactory writers) : IRuntimeIntrinsicEmitter
    {
        public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
            emitter.Emit(request, writers.Create(code, request));
    }

    // A static method of the named type whose call the emitter lowers.
    internal static RuntimeIntrinsicEmissionRequest Request(string ns, string type, string name)
    {
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.DelegateClone, [CliValueKind.ManagedReference]);
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference, CliValueKind.ManagedReference);
        var definition = new MethodDefinitionModel(new(Core, 0x06000100), new(Core, 0x02000100), name, true, signature, 1);
        return request with
        {
            Call = request.Call with
            {
                Method = new MethodInstanceModel(definition, CliTypeIdentity.Named(Core, ns, type, isValueType: false), [], signature),
            },
        };
    }

    private static void AssertCalls(EmitterTestSupport.RecordingInstructionWriter writer, RuntimeImportSymbol symbol)
    {
        var call = Assert.Single(writer.ToInstructions());
        Assert.Equal(WasmOpcodes.Call, call.Opcode);
        Assert.Equal((uint)Imports.Resolve(symbol, Everything), call.Operand.UnsignedValue);
    }

    private sealed class IndexCallingEmitter(uint index) : IRuntimeIntrinsicEmitter
    {
        public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(index)));
    }

    private sealed class CallingEmitter(RuntimeImportSymbol symbol) : IRuntimeIntrinsicEmitter
    {
        public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(
                (uint)Imports.Resolve(symbol, request.Instruction.Target.RuntimeImportSelection))));
    }
}
