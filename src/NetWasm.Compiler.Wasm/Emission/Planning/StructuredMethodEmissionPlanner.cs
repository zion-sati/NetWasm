using Microsoft.Extensions.Logging;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed partial class StructuredMethodEmissionPlanner(
    IManagedMethodIdentityFactory identities,
    ICilTypeIdentityResolver types,
    ILogger logger) : IStructuredMethodEmissionPlanner
{
    public ImmutableArray<StructuredMethodEmission> Plan(WasmEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definitions = request.Methods.Values
            .Select(method => (
                Method: method,
                Definition: method.Header.Method,
                MethodInstance: method.Header.MethodInstance,
                DeclaringType: method.Header.MethodInstance?.DeclaringType ??
                    types.Resolve(method.Header.Method.DeclaringType)))
            .Where(method => method.Definition.GenericArity == 0
                && !method.DeclaringType.ContainsGenericParameters)
            .Select(method => new StructuredMethodEmission(
                method.MethodInstance is null
                    ? identities.Create(method.Definition, method.DeclaringType)
                    : identities.Create(method.MethodInstance),
                method.Method))
            .OrderBy(method => method.Identity.CanonicalName, StringComparer.Ordinal)
            .ToImmutableArray();

        var constructed = request.ConstructedMethods
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                if (!request.MethodInstances.TryGetValue(pair.Key, out var method))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.CompilerInvariant,
                        $"MISSING_CONSTRUCTED_METHOD_INSTANCE: '{pair.Key}'."));
                }

                var identity = identities.Create(method);
                if (!StringComparer.Ordinal.Equals(identity.CanonicalName, pair.Key))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.CompilerInvariant,
                        $"CONSTRUCTED_METHOD_IDENTITY_MISMATCH: expected '{pair.Key}', actual '{identity.CanonicalName}'."));
                }

                return new StructuredMethodEmission(identity, pair.Value);
            })
            .ToImmutableArray();


        var plannedDefinitions = definitions
            .Select(method => method.Method.Header.Method.Key)
            .ToHashSet();
        var excludedDefinitions = request.Methods.Keys
            .Where(method => !plannedDefinitions.Contains(method))
            .OrderBy(method => method.ToString(), StringComparer.Ordinal)
            .ToArray();

        LogPlan(
            logger,
            definitions.Length,
            constructed.Length,
            request.CallableMethods.Count,
            request.Methods.Count,
            excludedDefinitions.Length);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var method in excludedDefinitions)
            {
                LogExcludedDefinition(logger, method);
            }
        }
        return definitions.Concat(constructed).ToImmutableArray();
    }

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Debug,
        Message = "Structured method planning selected {DefinitionCount} direct definitions and {ConstructedCount} constructed methods from {CallableCount} callable identities and {StructuredCount} structured definitions; {ExcludedCount} structured definitions remained open after generic binding.")]
    private static partial void LogPlan(
        ILogger logger,
        int definitionCount,
        int constructedCount,
        int callableCount,
        int structuredCount,
        int excludedCount);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Trace,
        Message = "Structured definition {MethodKey} was excluded because its declaring type or method remained open after generic binding.")]
    private static partial void LogExcludedDefinition(ILogger logger, EntityKey methodKey);

}
