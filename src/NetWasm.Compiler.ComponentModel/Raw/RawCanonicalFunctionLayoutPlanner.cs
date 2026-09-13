using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawCanonicalFunctionLayoutPlanner
{
    RawCanonicalFunctionLayout Plan(CanonicalAbiFunction abiFunction, WasmTarget target);
}

public sealed class RawCanonicalFunctionLayoutPlanner(
    IRawCanonicalImportIdentityFormatter identities,
    ICanonicalAbiSignaturePlanner signatures,
    ICanonicalAbiMemoryLayoutPlanner memory) : IRawCanonicalFunctionLayoutPlanner
{
    private readonly IRawCanonicalImportIdentityFormatter _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ICanonicalAbiSignaturePlanner _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));
    private readonly ICanonicalAbiMemoryLayoutPlanner _memory = memory ??
        throw new ArgumentNullException(nameof(memory));

    public RawCanonicalFunctionLayout Plan(CanonicalAbiFunction abiFunction, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(abiFunction);
        ArgumentNullException.ThrowIfNull(abiFunction.InterfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiFunction.FunctionName);
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (abiFunction.Kind != CanonicalAbiFunctionKind.Function)
        {
            throw ComponentException.Invalid("resource built-ins require their own raw binding plan");
        }
        if (abiFunction.Parameters.IsDefault)
        {
            throw ComponentException.Invalid("raw function parameters must be explicit");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fields = ImmutableArray.CreateBuilder<CanonicalAbiField>();
        foreach (var parameter in abiFunction.Parameters)
        {
            if (parameter is null || parameter.Type is null ||
                string.IsNullOrWhiteSpace(parameter.Name) || !names.Add(parameter.Name))
            {
                throw ComponentException.Invalid("raw function parameters must have unique names and explicit types");
            }
            fields.Add(new(parameter.Name, parameter.Type));
        }

        var identity = _identities.Format(abiFunction, target);
        var signature = _signatures.Plan(abiFunction, CanonicalAbiDirection.LoweredImport);
        var parameterType = new CanonicalAbiType(
            CanonicalAbiTypeKind.Tuple, CliTypeIdentity.FromStackKind(CliValueKind.Unknown))
        {
            Fields = fields.ToImmutable(),
        };
        var parameterMemory = _memory.Plan(parameterType, target);
        var resultMemory = abiFunction.Result is null ? null : _memory.Plan(abiFunction.Result, target);
        return new(target, abiFunction, identity.Module, identity.Name, signature, parameterMemory, resultMemory);
    }
}
