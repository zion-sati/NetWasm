using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

/// <summary>
/// Plans a stream or future built-in: its fixed core signature and the payload type an
/// adapter copies elements of.
/// </summary>
public sealed class RawPayloadImportLayoutBuilder(IWitCanonicalTypeResolver types) : IRawWitImportLayoutBuilder
{
    private readonly IWitCanonicalTypeResolver _types = types ?? throw new ArgumentNullException(nameof(types));

    public RawWitImportLayout Build(RawWitImportLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Declaration is not RawWitImportDeclaration.Payload payload)
        {
            throw ComponentException.Invalid("payload layout builder requires a payload declaration");
        }
        var type = _types.Resolve(request.Document, new WitTypeReference.Defined(payload.TypeId));
        var stream = type.Kind == CanonicalAbiTypeKind.Stream;
        if (!stream && type.Kind != CanonicalAbiTypeKind.Future
            || IsStreamOperation(payload.Operation) != stream)
        {
            throw ComponentException.Invalid("raw payload built-in does not match its stream or future type");
        }
        var identity = new RawCanonicalImportIdentity(
            payload.InterfaceName.Length == 0 ? "$root" : payload.InterfaceName,
            PayloadName(payload));
        return new RawWitImportLayout.Payload(
            request.Target,
            identity,
            Signature(payload.Operation),
            payload,
            type);
    }

    private static string PayloadName(RawWitImportDeclaration.Payload payload)
    {
        var (operation, asynchronous) = payload.Operation switch
        {
            RawPayloadOperation.StreamNew => ("stream-new", false),
            RawPayloadOperation.StreamRead => ("stream-read", true),
            RawPayloadOperation.StreamWrite => ("stream-write", true),
            RawPayloadOperation.StreamDropReadable => ("stream-drop-readable", false),
            RawPayloadOperation.StreamDropWritable => ("stream-drop-writable", false),
            RawPayloadOperation.FutureNew => ("future-new", false),
            RawPayloadOperation.FutureRead => ("future-read", true),
            RawPayloadOperation.FutureWrite => ("future-write", true),
            RawPayloadOperation.FutureDropReadable => ("future-drop-readable", false),
            _ => ("future-drop-writable", false),
        };
        return $"{(asynchronous ? "[async-lower]" : "")}[{operation}-{payload.Index}]{payload.Function.Name}";
    }

    private static bool IsStreamOperation(RawPayloadOperation operation) => operation is
        RawPayloadOperation.StreamNew or RawPayloadOperation.StreamRead or RawPayloadOperation.StreamWrite
        or RawPayloadOperation.StreamDropReadable or RawPayloadOperation.StreamDropWritable;

    // new: () -> readable | writable << 32; a stream copy: (end, buffer, count) -> copy
    // status; a future copy: (end, buffer) -> copy status; a drop: (end) -> ().
    private static CanonicalAbiCoreSignature Signature(RawPayloadOperation operation)
    {
        ImmutableArray<CliValueKind> parameters = operation switch
        {
            RawPayloadOperation.StreamNew or RawPayloadOperation.FutureNew => [],
            RawPayloadOperation.StreamRead or RawPayloadOperation.StreamWrite =>
                [CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
            RawPayloadOperation.FutureRead or RawPayloadOperation.FutureWrite =>
                [CliValueKind.I4, CliValueKind.ManagedAddress],
            _ => [CliValueKind.I4],
        };
        var result = operation switch
        {
            RawPayloadOperation.StreamNew or RawPayloadOperation.FutureNew => CliValueKind.I8,
            RawPayloadOperation.StreamRead or RawPayloadOperation.StreamWrite
                or RawPayloadOperation.FutureRead or RawPayloadOperation.FutureWrite => CliValueKind.I4,
            _ => CliValueKind.Void,
        };
        return new(parameters, result, parameters,
            result == CliValueKind.Void ? [] : [result], false, false);
    }
}
