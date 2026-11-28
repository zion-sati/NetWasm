// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.CompositeFormats;

internal interface ICompositeFormatCallResolver
{
    // The call when the instruction calls string.Format(string, object...) or string.Format(IFormatProvider, string,
    // object...) with one to three objects, directly or through a method whose body only passes its parameters on to
    // it, as each library's SR.Format does; null for any other instruction.
    CompositeFormatCall? Resolve(CilInstruction instruction);
}

// How many objects follow the format, and whether a provider comes before it; for the params overload, an array of
// them, whose length the call site decides.
internal readonly record struct CompositeFormatCall(int Arguments, bool Provider, bool Params = false);

internal sealed class CompositeFormatCallResolver(
    ICalledMethodResolver calledMethods,
    ICalleeMethodBodyReader calleeBodies) : ICompositeFormatCallResolver
{
    private const int MaximumArguments = 3;

    // By method: the call it makes, or none for a method that neither is string.Format nor passes its parameters on.
    private readonly ConcurrentDictionary<EntityKey, CompositeFormatCall?> _methods = new();

    public CompositeFormatCall? Resolve(CilInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Operation != CilOperation.Call ||
            calledMethods.Resolve(instruction) is not { } method ||
            Shape(method) is not { } shape)
        {
            return null;
        }
        return _methods.GetOrAdd(method.Definition.Key, _ => IsFormat(method) || Forwards(method, shape) ? shape : null);
    }

    // A static, non-generic method returning a string of an optional IFormatProvider, a string and one to three
    // objects or an array of objects.
    private static CompositeFormatCall? Shape(MethodInstanceModel method)
    {
        var definition = method.Definition;
        var parameters = definition.Signature.ParameterSignatureTypes;
        if (!definition.IsStatic || definition.GenericArity != 0 || !method.DeclaringType.TypeArguments.IsEmpty ||
            parameters.Length < 2 || !IsString(definition.Signature.ReturnSignatureType))
        {
            return null;
        }
        var provider = parameters[0].FullName == "System.IFormatProvider";
        var first = provider ? 2 : 1;
        if (parameters.Length - first is < 1 or > MaximumArguments || !IsString(parameters[first - 1]))
        {
            return null;
        }
        if (parameters.Length - first == 1 &&
            parameters[first] is { Shape: CliTypeShape.SzArray, ElementType.CanonicalName: "primitive:object" })
        {
            return new CompositeFormatCall(0, provider, Params: true);
        }
        for (var index = first; index < parameters.Length; index++)
        {
            if (parameters[index].CanonicalName != "primitive:object")
            {
                return null;
            }
        }
        return new CompositeFormatCall(parameters.Length - first, provider);
    }

    private static bool IsFormat(MethodInstanceModel method) =>
        method.Definition.Name == "Format" && method.DeclaringType.CanonicalName == "primitive:string";

    // Its body loads each parameter in order, calls string.Format with them and returns what it returns. A params
    // forwarder may first return the format for a null array, as `args is null ? format : string.Format(format,
    // args)` does: a call that passes an array it builds reaches string.Format all the same.
    private bool Forwards(MethodInstanceModel method, CompositeFormatCall shape)
    {
        var parameters = (shape.Params ? 1 : shape.Arguments) + (shape.Provider ? 2 : 1);
        if (!calleeBodies.TryReadMethodBody(method, out var body) || !body.ExceptionRegions.IsEmpty)
        {
            return false;
        }
        if (shape.Params && body.Instructions.Length == parameters + 6 &&
            Loads(body.Instructions[0], parameters - 1) &&
            body.Instructions[1] is { Operation: CilOperation.BranchIfFalse, Operand: CilOperand.BranchTarget { Offset: var nullTarget } } &&
            nullTarget == body.Instructions[parameters + 4].Offset &&
            Loads(body.Instructions[parameters + 4], parameters - 2) &&
            body.Instructions[parameters + 5].Operation == CilOperation.Return)
        {
            body = body with { Instructions = body.Instructions.Slice(2, parameters + 2) };
        }
        if (body.Instructions.Length != parameters + 2)
        {
            return false;
        }
        for (var index = 0; index < parameters; index++)
        {
            if (!Loads(body.Instructions[index], index))
            {
                return false;
            }
        }
        var call = body.Instructions[parameters];
        return call.Operation == CilOperation.Call &&
            calledMethods.Resolve(call) is { } target &&
            IsFormat(target) &&
            Shape(target) == shape &&
            body.Instructions[parameters + 1].Operation == CilOperation.Return;
    }

    private static bool IsString(CliTypeIdentity type) => type.CanonicalName == "primitive:string";

    private static bool Loads(CilInstruction instruction, int parameter) =>
        instruction is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } &&
        argument == parameter;
}
