using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: classify the bound public API, before its shared implementation
// loses the caller's target and filter provenance.
internal sealed class AttributeQueryClassifier(
    ImmutableDictionary<EntityKey, AttributeQueryApiKind> declaringTypes,
    CliTypeIdentity memberInfoType,
    CliTypeIdentity typeType) : IAttributeQueryClassifier
{
    private static readonly ImmutableDictionary<string, AttributeQueryOperation> Operations =
        ImmutableDictionary<string, AttributeQueryOperation>.Empty
            .Add("IsDefined", AttributeQueryOperation.IsDefined)
            .Add("GetCustomAttribute", AttributeQueryOperation.GetOne)
            .Add("GetCustomAttributes", AttributeQueryOperation.GetMany);

    public AttributeQueryCall? Classify(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!declaringTypes.TryGetValue(method.Definition.DeclaringType, out var api))
        {
            return null;
        }
        if (!Operations.TryGetValue(method.Definition.Name, out var operation))
        {
            if (method.Definition.Name is "get_CustomAttributes" or "GetCustomAttributesData")
            {
                throw Unsupported("Custom attribute metadata enumeration is not supported; use a filtered type query.");
            }
            return null;
        }

        var parameters = method.Signature.ParameterSignatureTypes;
        var generic = method.MethodArguments.Length == 1;
        var targetParameters = api == AttributeQueryApiKind.Instance ? 0 : 1;
        var filteredParameters = targetParameters + (generic ? 0 : 1);
        var hasInherit = parameters.Length == filteredParameters + 1;
        if (api == AttributeQueryApiKind.Instance &&
            (method.Definition.IsStatic || generic || operation == AttributeQueryOperation.GetOne || !hasInherit) ||
            api != AttributeQueryApiKind.Instance &&
            (!method.Definition.IsStatic || parameters.IsEmpty || !parameters[0].Equals(memberInfoType)) ||
            generic && (api != AttributeQueryApiKind.Extension || operation == AttributeQueryOperation.IsDefined) ||
            method.MethodArguments.Length > 1 ||
            parameters.Length != filteredParameters && !hasInherit ||
            !generic && (parameters.Length <= targetParameters || !parameters[targetParameters].Equals(typeType)) ||
            hasInherit && parameters[^1].StackKind != CliValueKind.I4)
        {
            throw Unsupported("This custom attribute overload is not supported; query one type with an explicit attribute filter.");
        }

        return new(operation,
            parameters.Length + (api == AttributeQueryApiKind.Instance ? 1 : 0),
            generic ? null : 1,
            generic ? method.MethodArguments[0] : null,
            hasInherit ? parameters.Length - (api == AttributeQueryApiKind.Instance ? 0 : 1) : null);
    }

    private static CompilerException Unsupported(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, message));
}
