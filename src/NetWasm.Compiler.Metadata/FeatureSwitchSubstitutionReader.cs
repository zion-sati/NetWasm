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
// (Microsoft.Extensions packages stub System.SR.UsingResourceKeys() for System.Resources.UseSystemResourceKeys).
// NetWasm applies the stubs of static, parameterless Boolean methods; it leaves other substitutions unapplied, so
// those methods keep their own bodies.
internal static class FeatureSwitchSubstitutionReader
{
    private const string ResourceName = "ILLink.Substitutions.xml";

    // The stubs by method token.
    internal static Dictionary<int, ImmutableArray<FeatureSwitchStub>> Read(PEReader image, MetadataReader reader)
    {
        var stubs = new Dictionary<int, ImmutableArray<FeatureSwitchStub>>();
        if (Resource(image, reader) is not { } document)
            return stubs;
        var types = reader.TypeDefinitions.ToDictionary(handle => FullName(reader, handle), StringComparer.Ordinal);
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
                        !bool.TryParse(method.Attribute("value")?.Value, out var result) ||
                        method.Attribute("signature")?.Value is not { } signature ||
                        !signature.StartsWith("System.Boolean ", StringComparison.Ordinal) ||
                        !signature.EndsWith("()", StringComparison.Ordinal))
                        continue;
                    var name = signature["System.Boolean ".Length..^2];
                    var conditions = Conditions(method, typeConditions);
                    if (conditions.IsEmpty)
                        continue;
                    foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                    {
                        var definition = reader.GetMethodDefinition(methodHandle);
                        if ((definition.Attributes & System.Reflection.MethodAttributes.Static) == 0 ||
                            reader.GetString(definition.Name) != name ||
                            definition.GetParameters().Count != 0)
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

    // An element's feature condition joins its parents'; an element whose condition cannot be read applies nowhere.
    private static ImmutableArray<(string Feature, bool Value)> Conditions(
        XElement element, ImmutableArray<(string Feature, bool Value)> parent)
    {
        if (element.Attribute("feature")?.Value is not { } feature)
            return parent;
        return bool.TryParse(element.Attribute("featurevalue")?.Value, out var value) ? parent.Add((feature, value)) : [];
    }

    private static XDocument? Resource(PEReader image, MetadataReader reader)
    {
        foreach (var handle in reader.ManifestResources)
        {
            var resource = reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil || reader.GetString(resource.Name) != ResourceName)
                continue;
            // An image with metadata has its CLI header.
            var resources = image.PEHeaders.CorHeader!.ResourcesDirectory;
            if (resources.Size <= 0)
                return null;
            var data = image.GetSectionData(resources.RelativeVirtualAddress).GetReader();
            data.Offset = checked((int)resource.Offset);
            var length = data.ReadInt32();
            try
            {
                using var stream = new MemoryStream(data.ReadBytes(length));
                return XDocument.Load(stream);
            }
            catch (XmlException)
            {
                return null;
            }
        }
        return null;
    }

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return FullName(reader, declaring) + "/" + name;
        var space = reader.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }
}
