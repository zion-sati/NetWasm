using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawCliCoreSignatureProjector
{
    RawCoreFunctionImportSignature Project(
        RawCliFunctionImportSignature signature,
        WasmTarget target);
}

public sealed class RawCliCoreSignatureProjector : IRawCliCoreSignatureProjector
{
    public RawCoreFunctionImportSignature Project(
        RawCliFunctionImportSignature signature,
        WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(signature.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature.Identity.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature.Identity.Name);
        if (signature.Parameters.IsDefault)
        {
            throw ComponentException.Invalid("raw CLI import parameters must be explicit");
        }
        ValidateTarget(target);
        var parameters = ImmutableArray.CreateBuilder<RawCoreValueType>();
        foreach (var parameter in signature.Parameters)
        {
            if (parameter == CliValueKind.Void)
            {
                throw ComponentException.Invalid("raw CLI import parameter cannot be void");
            }
            parameters.Add(ProjectValue(parameter, target));
        }
        var results = signature.Result == CliValueKind.Void
            ? ImmutableArray<RawCoreValueType>.Empty
            : [ProjectValue(signature.Result, target)];
        return new(signature.Identity, parameters.ToImmutable(), results);
    }

    private static RawCoreValueType ProjectValue(CliValueKind value, WasmTarget target) =>
        value switch
        {
            CliValueKind.I4 or CliValueKind.ValueType => RawCoreValueType.I32,
            CliValueKind.I8 => RawCoreValueType.I64,
            CliValueKind.F4 => RawCoreValueType.F32,
            CliValueKind.F8 => RawCoreValueType.F64,
            CliValueKind.NativeInt or CliValueKind.ManagedReference or CliValueKind.ManagedAddress =>
                target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32,
            _ => throw ComponentException.Invalid("raw CLI import contains an unsupported stack kind"),
        };

    private static void ValidateTarget(WasmTarget target)
    {
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
