// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// How a method returns a value type (a Strategy; IValueTypeReturnConventionFactory creates the one for a return type): it copies
// the value into the buffer its caller passes as its first parameter, or returns the value's fields in Wasm results.
internal interface IValueTypeReturnConvention
{
    // Returns the value at a ret, leaving the method's frames (emitFrameExit) on the way; sourceLocal names the
    // evaluation stack local that holds the value's address, asked only where the value is read from it.
    void Emit(IWasmInstructionWriter code, ValueTypeReturn returned, Func<int> sourceLocal,
        Action<IWasmInstructionWriter> emitFrameExit);
}

// A ret of a value type: the method's context and the ret's offset.
internal sealed record ValueTypeReturn(MethodEmissionContext Context, int Offset);

internal interface IValueTypeReturnConventionFactory
{
    IValueTypeReturnConvention Create(CliTypeIdentity returnType);
}

internal sealed class ValueTypeReturnConventionFactory(
    IStructResultClassifier classifier,
    IStructResultEmitter fields,
    IValueLayoutProvider values,
    IValueCopyEmitter valueCopies) : IValueTypeReturnConventionFactory
{
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly IStructResultEmitter _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IValueLayoutProvider _values = values ?? throw new ArgumentNullException(nameof(values));
    private readonly IValueCopyEmitter _valueCopies = valueCopies ?? throw new ArgumentNullException(nameof(valueCopies));

    public IValueTypeReturnConvention Create(CliTypeIdentity returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        return _classifier.Classify(returnType) is { } shape
            ? new FieldReturnConvention(shape, _fields)
            : new BufferReturnConvention(_values.GetValueLayout(returnType), _valueCopies);
    }
}

// The value is copied into the caller's buffer, unless the method built it there (ReturnBufferLocals).
internal sealed class BufferReturnConvention(ValueLayout layout, IValueCopyEmitter valueCopies) : IValueTypeReturnConvention
{
    public void Emit(IWasmInstructionWriter code, ValueTypeReturn returned, Func<int> sourceLocal,
        Action<IWasmInstructionWriter> emitFrameExit)
    {
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(sourceLocal);
        ArgumentNullException.ThrowIfNull(emitFrameExit);
        if (returned.Context.ValueLayout.ReturnBufferLocal?.Return != returned.Offset)
        {
            var source = sourceLocal();
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(0)));
            valueCopies.Emit(
                code,
                0,
                layout.Size,
                layout.Alignment,
                SmallBlockEmitter.WidestChunk(layout),
                writer => writer.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)source))),
                returned.Context);
        }
        emitFrameExit(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
    }
}

// The value's fields are read before the frames it may live in are left, and returned: nothing between can collect,
// so the references among them need no root. An argument read in place is returned from its parameters, and a local
// in the return lanes from those lanes.
internal sealed class FieldReturnConvention(StructResultShape shape, IStructResultEmitter fields) : IValueTypeReturnConvention
{
    public void Emit(IWasmInstructionWriter code, ValueTypeReturn returned, Func<int> sourceLocal,
        Action<IWasmInstructionWriter> emitFrameExit)
    {
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(sourceLocal);
        ArgumentNullException.ThrowIfNull(emitFrameExit);
        var context = returned.Context;
        if (context.ReturnedFieldArguments.TryGetValue(returned.Offset, out var returnedArgument))
        {
            var inPlace = context.InPlaceFieldArguments[returnedArgument];
            emitFrameExit(code);
            for (var lane = 0; lane < inPlace.Shape.Results.Length; lane++) Get(code, inPlace.FirstParameter + lane);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            return;
        }
        if (context.ReturnLanes is { } lanes && lanes.Returns.Contains(returned.Offset))
        {
            emitFrameExit(code);
            foreach (var local in context.StructResultLocals.For(shape)) Get(code, local);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            return;
        }
        var source = sourceLocal();
        var locals = context.StructResultLocals.For(shape);
        fields.Emit(code, shape, writer => Get(writer, source), context.VectorScratch);
        for (var index = locals.Length - 1; index >= 0; index--)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)locals[index])));
        }
        emitFrameExit(code);
        foreach (var local in locals) Get(code, local);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}
