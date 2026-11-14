using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;
using System.Xml.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Reads an assembly's embedded ILLink.Substitutions.xml, the feature-switch substitutions .NET's trimmer applies
// (Microsoft.Extensions packages stub System.SR.UsingResourceKeys() for System.Resources.UseSystemResourceKeys; CoreLib
// stubs EventSource.IsEnabled() for System.Diagnostics.Tracing.EventSource.IsSupported). NetWasm applies the method
// stubs of Boolean methods, which return the stub's value, and of void methods, which do nothing, static or instance
// and whatever their parameters, matched by signature as ILLink writes it; it leaves other substitutions (other return
// types, fields, resources) unapplied, so those keep their own bodies. An Adapter from the substitution document to
// stubs: the resource, and the names of types and signatures, come from its collaborators.
internal sealed class FeatureSwitchSubstitutionReader(
    IEmbeddedResourceReader resources,
    ITypeDefinitionNameFormatter typeNames,
    IMethodSignatureFormatter signatures) : IFeatureSwitchSubstitutionReader
{
    private const string ResourceName = "ILLink.Substitutions.xml";

    private readonly IEmbeddedResourceReader _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly ITypeDefinitionNameFormatter _typeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
    private readonly IMethodSignatureFormatter _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));

    public Dictionary<int, ImmutableArray<FeatureSwitchStub>> Read(PEReader image, MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reader);
        var stubs = new Dictionary<int, ImmutableArray<FeatureSwitchStub>>();
        if (Document(_resources.Read(image, reader, ResourceName)) is not { } document)
            return stubs;
        var types = reader.TypeDefinitions.ToDictionary(handle => _typeNames.Format(reader, handle), StringComparer.Ordinal);
        // A document that loaded has its root element.
        foreach (var assembly in document.Root!.Elements("assembly"))
        {
            var assemblyConditions = Conditions(assembly, []);
            foreach (var type in assembly.Elements("type"))
            {
                if (type.Attribute("fullname")?.Value is not { } typeName || !types.TryGetValue(typeName, out var typeHandle))
                    continue;
                var typeConditions = Conditions(type, assemblyConditions);
                foreach (var method in type.Elements("method"))
                {
                    if (method.Attribute("body")?.Value != "stub" ||
                        method.Attribute("signature")?.Value is not { } signature ||
                        Result(signature, method.Attribute("value")?.Value) is not { } result)
                        continue;
                    var conditions = Conditions(method, typeConditions);
                    if (conditions.IsEmpty)
                        continue;
                    foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                    {
                        if (_signatures.Format(reader, reader.GetMethodDefinition(methodHandle)) != signature)
                            continue;
                        var token = MetadataTokens.GetToken(methodHandle);
                        stubs[token] = (stubs.TryGetValue(token, out var existing) ? existing : [])
                            .Add(new FeatureSwitchStub(conditions, result));
                    }
                }
            }
        }
        return stubs;
    }

    // A Boolean method's stub returns its value, and a void method's does nothing (its result is unused); a stub of
    // another return type, or a Boolean one without a readable value, applies nowhere.
    private static bool? Result(string signature, string? value) =>
        signature.StartsWith("System.Void ", StringComparison.Ordinal) ? false
        : signature.StartsWith("System.Boolean ", StringComparison.Ordinal) && bool.TryParse(value, out var result) ? result
        : null;

    // An element's feature condition joins its parents'; an element whose condition cannot be read applies nowhere.
    private static ImmutableArray<(string Feature, bool Value)> Conditions(
        XElement element, ImmutableArray<(string Feature, bool Value)> parent)
    {
        if (element.Attribute("feature")?.Value is not { } feature)
            return parent;
        return bool.TryParse(element.Attribute("featurevalue")?.Value, out var value) ? parent.Add((feature, value)) : [];
    }

    // A resource that is not well-formed XML applies nothing.
    private static XDocument? Document(byte[]? resource)
    {
        if (resource is null)
            return null;
        try
        {
            using var stream = new MemoryStream(resource);
            return XDocument.Load(stream);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
