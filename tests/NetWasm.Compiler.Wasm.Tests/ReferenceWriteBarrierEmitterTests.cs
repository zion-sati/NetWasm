// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A program compiled for a collector with generations records every store of a value that holds a reference;
// a program compiled for any other collector emits nothing.
public sealed class ReferenceWriteBarrierEmitterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity Holder = CliTypeIdentity.Named(Assembly, "Fixture", "Holder", isValueType: true);
    private static readonly CliTypeIdentity Plain = CliTypeIdentity.Named(Assembly, "Fixture", "Plain", isValueType: true);
    private static readonly RuntimeImportSelection Recording =
        new(WasmModuleProfile.CoreApplication, false, IncludeReferenceWriteBarrier: true);
    private static readonly RuntimeImportSelection Silent = new(WasmModuleProfile.CoreApplication, false);

    public static TheoryData<string> RecordedTypes() => ["a reference", "a value that holds one"];

    [Theory]
    [MemberData(nameof(RecordedTypes))]
    public void AStoreOfAValueThatHoldsAReferenceIsRecordedAtItsAddress(string stored)
    {
        var (emitter, catalog) = Create();
        var request = Request(Recording);
        var code = new RecordingInstructionWriter();

        emitter.Emit(request, code, 5, stored == "a reference" ? Reference : Holder);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.Call], instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(5u, instructions[0].Operand.UnsignedValue);
        // The module's index of the import, which its selection decides.
        Assert.Equal((uint)catalog.Resolve(RuntimeImportSymbol.RecordReferenceWrite, Recording),
            instructions[1].Operand.UnsignedValue);
    }

    public static TheoryData<CliValueKind> ScalarKinds() =>
        [CliValueKind.I4, CliValueKind.I8, CliValueKind.F8, CliValueKind.NativeInt, CliValueKind.ManagedAddress];

    [Theory]
    [MemberData(nameof(ScalarKinds))]
    public void AStoreOfAScalarIsNotRecorded(CliValueKind kind)
    {
        var (emitter, _) = Create();
        var code = new RecordingInstructionWriter();

        emitter.Emit(Request(Recording), code, 5, CliTypeIdentity.FromStackKind(kind));

        Assert.Empty(code.ToInstructions());
    }

    // A filter funclet's context carries no imports of its own: its stores are recorded all the same.
    [Fact]
    public void TheModuleDecidesWhateverTheMethodsContextSays()
    {
        var (emitter, _) = Create();
        var request = Request(Recording);
        var code = new RecordingInstructionWriter();
        Assert.False(request.Context.RuntimeImportSelection.IncludeReferenceWriteBarrier);

        emitter.Emit(request, code, 5, Reference);

        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.Call], code.ToInstructions().Select(instruction => instruction.Opcode));
    }

    [Fact]
    public void AStoreOfAValueWithoutReferencesIsNotRecorded()
    {
        var (emitter, _) = Create();
        var code = new RecordingInstructionWriter();

        emitter.Emit(Request(Recording), code, 5, Plain);
        Rememberer().Remember(Request(Recording), code, Plain);
        emitter.Emit(Request(Recording), code, Plain);
        emitter.Emit(Request(Recording), code, _ => Assert.Fail("no address is needed"), Plain);

        Assert.Empty(code.ToInstructions());
    }

    [Fact]
    public void AProgramForACollectorWithoutGenerationsRecordsNothing()
    {
        var (emitter, _) = Create();
        var code = new RecordingInstructionWriter();
        var request = Request(Silent);

        emitter.Emit(request, code, 5, Reference);
        emitter.Emit(request, code, 5, Holder);
        Rememberer().Remember(request, code, Reference);
        emitter.Emit(request, code, Reference);
        emitter.Emit(request, code, _ => Assert.Fail("no address is needed"), Reference);
        emitter.Emit(request, code, 5, 6);

        Assert.Empty(code.ToInstructions());
    }

    // An array element's address exists only on the stack: it is copied to the method's address scratch local
    // before the store and read from there after it.
    [Fact]
    public void AnAddressOnTheStackIsRememberedBeforeTheStoreAndRecordedAfterIt()
    {
        var (emitter, catalog) = Create();
        var request = Request(Recording);
        var code = new RecordingInstructionWriter();

        Rememberer().Remember(request, code, Reference);
        emitter.Emit(request, code, Reference);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.LocalTee, WasmOpcodes.LocalGet, WasmOpcodes.Call],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)request.Context.ObjectTemporary, instructions[0].Operand.UnsignedValue);
        Assert.Equal((uint)request.Context.ObjectTemporary, instructions[1].Operand.UnsignedValue);
        Assert.Equal((uint)catalog.Resolve(RuntimeImportSymbol.RecordReferenceWrite, Recording),
            instructions[2].Operand.UnsignedValue);
    }

    [Fact]
    public void AnAddressTheCallerWorksOutIsWrittenBeforeTheRecord()
    {
        var (emitter, catalog) = Create();
        var code = new RecordingInstructionWriter();

        emitter.Emit(Request(Recording), code,
            writer => writer.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(64))),
            Reference);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.I32Constant, WasmOpcodes.Call], instructions.Select(instruction => instruction.Opcode));
        Assert.Equal((uint)catalog.Resolve(RuntimeImportSymbol.RecordReferenceWrite, Recording),
            instructions[1].Operand.UnsignedValue);
    }

    [Fact]
    public void ARangeIsRecordedByItsStartAndLength()
    {
        var (emitter, catalog) = Create();
        var code = new RecordingInstructionWriter();

        emitter.Emit(Request(Recording), code, 5, 6);

        var instructions = code.ToInstructions();
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call],
            instructions.Select(instruction => instruction.Opcode));
        Assert.Equal([5u, 6u], instructions.Take(2).Select(instruction => instruction.Operand.UnsignedValue));
        Assert.Equal((uint)catalog.Resolve(RuntimeImportSymbol.RecordReferenceWrites, Recording),
            instructions[2].Operand.UnsignedValue);
    }

    // Both imports follow every import a program without generations has, so leaving them out moves no other.
    [Fact]
    public void TheImportsAreTheLastAndOnlyAProgramThatRecordsHasThem()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var all = Recording with
        {
            IncludeStackTrace = true,
            IncludeExceptionCapture = true,
            IncludeEphemeronHandles = true,
            IncludeFinalization = true,
            IncludeWeakHandles = true,
            IncludeRandomSeed = true,
            IncludeArraySetValue = true,
            IncludeUnhandledExceptionHint = true,
            IncludeObjectClone = true,
            IncludeUninitializedArrays = true,
            IncludeTerminalExceptionReporter = true,
        };
        var without = all with { IncludeReferenceWriteBarrier = false };

        var recording = catalog.Resolve(all);
        var silent = catalog.Resolve(without);

        Assert.Equal(silent.Length + 2, recording.Length);
        Assert.Equal(silent, recording.Take(silent.Length));
        Assert.Equal(["record_reference_write", "record_reference_writes"],
            recording.Skip(silent.Length).Select(import => import.Name));
        Assert.False(without.Includes(RuntimeImportSymbol.RecordReferenceWrite));
        Assert.False(without.Includes(RuntimeImportSymbol.RecordReferenceWrites));
        Assert.True(all.Includes(RuntimeImportSymbol.RecordReferenceWrite));
        Assert.True(all.Includes(RuntimeImportSymbol.RecordReferenceWrites));
    }

    [Fact]
    public void ItRejectsMissingCollaboratorsAndArguments()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var values = new Layouts();
        var emitter = new ReferenceWriteBarrierEmitter(catalog, new ReferenceWriteRecordPolicy(values));
        var request = Request(Recording);
        var code = new RecordingInstructionWriter();

        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteBarrierEmitter(null!, new ReferenceWriteRecordPolicy(values)));
        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteBarrierEmitter(catalog, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code, 0, Reference));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, 0, Reference));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, code, 0, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, code, null!, Reference));
        Assert.Throws<ArgumentNullException>(() => Rememberer().Remember(null!, code, Reference));
        Assert.Throws<ArgumentNullException>(() => Rememberer().Remember(request, null!, Reference));
        Assert.Throws<ArgumentNullException>(() => Rememberer().Remember(request, code, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code, Reference));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code, 0, 1));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, 0, 1));
        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteAddressRememberer(null!));
        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteRecordPolicy(null!));
        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteRecordPolicy(values).Records(null!, Reference));
        Assert.Throws<ArgumentNullException>(() => new ReferenceWriteRecordPolicy(values).Records(request, null!));
    }

    // The rememberer keeps the address exactly when the emitter records the store: one policy decides both.
    private static ReferenceWriteAddressRememberer Rememberer() =>
        new(new ReferenceWriteRecordPolicy(new Layouts()));

    private static (ReferenceWriteBarrierEmitter Emitter, IRuntimeImportResolver Catalog) Create()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        return (new ReferenceWriteBarrierEmitter(catalog, new ReferenceWriteRecordPolicy(new Layouts())), catalog);
    }

    // The module's imports decide; the method's context, as a filter funclet's, may say nothing.
    // A store the root map names, into an object the method allocated with no safepoint since, is recorded by none
    // of the ways a store is: the object is young.
    [Fact]
    public void AStoreIntoAYoungObjectIsNotRecorded()
    {
        var (emitter, _) = Create();
        var request = Request(Recording);
        request = request with
        {
            Context = request.Context with
            {
                RootMap = request.Context.RootMap with { UnrecordedStores = [request.Instruction.Offset] },
            },
        };
        var code = new RecordingInstructionWriter();

        emitter.Emit(request, code, 5, Reference);
        Rememberer().Remember(request, code, Reference);
        emitter.Emit(request, code, Holder);
        emitter.Emit(request, code, _ => throw new InvalidOperationException("The address is not written."), Reference);

        Assert.Empty(code.ToInstructions());
    }

    private static InstructionEmissionRequest Request(RuntimeImportSelection selection)
    {
        var request = CreateInstructionRequest(CilOperation.Nop);
        return request with { Target = request.Target with { RuntimeImportSelection = selection } };
    }

    private sealed class Layouts : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, 16, 8, type.Equals(Holder) ? [8] : []);
    }
}
