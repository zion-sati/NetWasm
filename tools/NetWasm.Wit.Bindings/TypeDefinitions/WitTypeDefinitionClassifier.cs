using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.TypeDefinitions;

public sealed class WitTypeDefinitionClassifier : IWitTypeDefinitionClassifier
{
    private static readonly FrozenDictionary<string, WitTypeDefinitionCategory> Categories =
        new Dictionary<string, WitTypeDefinitionCategory>(StringComparer.Ordinal)
        {
            ["record"] = WitTypeDefinitionCategory.Nominal,
            ["variant"] = WitTypeDefinitionCategory.Nominal,
            ["enum"] = WitTypeDefinitionCategory.Nominal,
            ["flags"] = WitTypeDefinitionCategory.Nominal,
            ["resource"] = WitTypeDefinitionCategory.Nominal,
            ["type"] = WitTypeDefinitionCategory.TransparentAlias,
            ["list"] = WitTypeDefinitionCategory.TransparentAlias,
            ["option"] = WitTypeDefinitionCategory.TransparentAlias,
            ["tuple"] = WitTypeDefinitionCategory.TransparentAlias,
            ["result"] = WitTypeDefinitionCategory.TransparentAlias,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public WitTypeDefinitionCategory Classify(WitTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var value = definition.Kind;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return WitTypeDefinitionCategory.Unsupported;
        }

        var properties = value.EnumerateObject();
        if (!properties.MoveNext())
        {
            return WitTypeDefinitionCategory.Unsupported;
        }

        var property = properties.Current;
        if (properties.MoveNext())
        {
            return WitTypeDefinitionCategory.Unsupported;
        }

        return Categories.TryGetValue(property.Name, out var category)
            ? category
            : WitTypeDefinitionCategory.Unsupported;
    }
}
