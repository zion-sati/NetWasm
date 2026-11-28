// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// How a caller passes an argument (a Strategy; IValueTypeArgumentConventionFactory creates the one for a parameter
// type): as the evaluation stack holds it (a value type by its address), or a value type by its fields, each a Wasm
// parameter of its own.
internal interface IValueTypeArgumentConvention
{
    // Pushes the argument emitValue pushes as the evaluation stack holds it.
    void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitValue, ValueTypeArgument argument);
}

// Where an argument is passed. A call in a managed method names the method's context, the call's offset and the
// argument's position (the receiver first), under which the method may have planned it: an argument of its own read in
// place and forwarded whole, or a temporary built in lanes.
internal sealed record ValueTypeArgument(
    MethodEmissionContext? Context = null,
    int Call = -1,
    int Position = -1,
    VectorScratch? Scratch = null);

internal interface IValueTypeArgumentConventionFactory
{
    // The convention for a parameter of the type; null (no declared parameter, as for a receiver) and every type other
    // than a classified struct pass as the evaluation stack holds them.
    IValueTypeArgumentConvention Create(CliTypeIdentity? parameterType);
}

// A struct the classifier accepts goes by its fields; anything else as the stack holds it.
internal sealed class ValueTypeArgumentConventionFactory(IStructResultClassifier classifier, IStructResultEmitter fields)
    : IValueTypeArgumentConventionFactory
{
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly IStructResultEmitter _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public IValueTypeArgumentConvention Create(CliTypeIdentity? parameterType) =>
        StructParameters.Shape(_classifier, parameterType) is { } shape
            ? new FieldArgumentConvention(shape, _fields)
            : StackArgumentConvention.Instance;
}

// Every argument as the stack holds it, for emitters built without the classifier.
internal sealed class StackArgumentConventionFactory : IValueTypeArgumentConventionFactory
{
    public IValueTypeArgumentConvention Create(CliTypeIdentity? parameterType) => StackArgumentConvention.Instance;
}

// The argument as the evaluation stack holds it.
internal sealed class StackArgumentConvention : IValueTypeArgumentConvention
{
    public static StackArgumentConvention Instance { get; } = new();

    public void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitValue, ValueTypeArgument argument)
    {
        ArgumentNullException.ThrowIfNull(emitValue);
        emitValue(code);
    }
}

// A value type passed by its fields: from the caller's parameters when the caller forwards its own argument read in
// place, from the lanes of a temporary built for the call, or read through the address the stack holds.
internal sealed class FieldArgumentConvention(StructResultShape shape, IStructResultEmitter fields) : IValueTypeArgumentConvention
{
    private readonly StructResultShape _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    private readonly IStructResultEmitter _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitValue, ValueTypeArgument argument)
    {
        ArgumentNullException.ThrowIfNull(emitValue);
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Context is { } context)
        {
            if (context.ForwardedFieldArguments.TryGetValue((argument.Call, argument.Position), out var forwarded))
            {
                var inPlace = context.InPlaceFieldArguments[forwarded];
                for (var lane = 0; lane < inPlace.Shape.Results.Length; lane++) Get(code, inPlace.FirstParameter + lane);
                return;
            }
            if (context.ArgumentLanes.Arguments.TryGetValue((argument.Call, argument.Position), out var temporary))
            {
                foreach (var lane in context.ArgumentLaneLocals[temporary]) Get(code, lane);
                return;
            }
        }
        _fields.Emit(code, _shape, emitValue, argument.Scratch);
    }

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
}
