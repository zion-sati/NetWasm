// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// How a caller receives a value type its callee returns (a Strategy; IValueTypeResultConventionFactory creates the one
// for a return type): through a buffer whose address the caller passes before the arguments, or as the value's fields
// in Wasm results, which the caller stores where the value goes.
internal interface IValueTypeResultConvention
{
    // The kinds of the locals received fields wait in before they are stored, in field order; none for a buffer.
    ImmutableArray<CliValueKind> FieldLocalKinds { get; }

    // Emits the call emitCall writes (its arguments, then the call itself) with the value's destination, the address
    // emitDestination pushes, on the side the convention takes it.
    void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitDestination, ValueTypeResultSite site,
        Action<IWasmInstructionWriter> emitCall);
}

// The locals a call's returned fields wait in, and whether the method keeps them in its call-result lanes instead of
// storing them.
internal sealed record ValueTypeResultSite(StructResultLocals Locals, VectorScratch? Scratch = null, bool InLanes = false);

internal interface IValueTypeResultConventionFactory
{
    // The convention of a call's value-type result (the return type's stack kind is ValueType).
    IValueTypeResultConvention Create(CliTypeIdentity returnType);
}

// A struct the classifier accepts (at most four scalar fields) comes back in results; any other through a buffer.
internal sealed class ValueTypeResultConventionFactory(IStructResultClassifier classifier, IStructResultEmitter fields)
    : IValueTypeResultConventionFactory
{
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    private readonly IStructResultEmitter _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public IValueTypeResultConvention Create(CliTypeIdentity returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        return _classifier.Classify(returnType) is { } shape
            ? new FieldResultConvention(shape, _fields)
            : BufferResultConvention.Instance;
    }
}

// Every value type through a buffer, for emitters built without the classifier.
internal sealed class BufferResultConventionFactory : IValueTypeResultConventionFactory
{
    public IValueTypeResultConvention Create(CliTypeIdentity returnType) => BufferResultConvention.Instance;
}

// The callee writes the value through the address it takes before its arguments.
internal sealed class BufferResultConvention : IValueTypeResultConvention
{
    public static BufferResultConvention Instance { get; } = new();

    public ImmutableArray<CliValueKind> FieldLocalKinds => [];

    public void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitDestination, ValueTypeResultSite site,
        Action<IWasmInstructionWriter> emitCall)
    {
        ArgumentNullException.ThrowIfNull(emitDestination);
        ArgumentNullException.ThrowIfNull(emitCall);
        emitDestination(code);
        emitCall(code);
    }
}

// The callee returns the value's fields in Wasm results: the caller takes them into its lanes, or stores each at the
// destination.
internal sealed class FieldResultConvention(StructResultShape shape, IStructResultEmitter fields) : IValueTypeResultConvention
{
    private readonly StructResultShape _shape = shape ?? throw new ArgumentNullException(nameof(shape));
    private readonly IStructResultEmitter _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public ImmutableArray<CliValueKind> FieldLocalKinds => _shape.Results;

    public void Emit(IWasmInstructionWriter code, Action<IWasmInstructionWriter> emitDestination, ValueTypeResultSite site,
        Action<IWasmInstructionWriter> emitCall)
    {
        ArgumentNullException.ThrowIfNull(emitDestination);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(emitCall);
        emitCall(code);
        var locals = site.Locals.For(_shape);
        if (site.InLanes)
        {
            CallResultLanes.Take(code, locals);
            return;
        }
        _fields.Emit(code, _shape, locals, emitDestination, site.Scratch);
    }
}
