using Mono.Cecil;
using System.Collections.Frozen;

namespace NetWasm.Sdk.Pack.References;

/// <summary>
/// Metadata projection strategy. Admission of framework identities belongs to the
/// SDK caller; this operation requires an explicit, validated set of shared types.
/// Original implementation bytes remain the translator's executable input.
/// </summary>
public sealed class FrameworkReferenceViewBuilder(IFrameworkReferenceTypeRewriter rewrite, IFrameworkTypeContractChecker checker) : IFrameworkReferenceViewBuilder
{
    public byte[] Build(FrameworkReferenceViewRequest request)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Implementation);
        ArgumentNullException.ThrowIfNull(request.Platform);
        ArgumentNullException.ThrowIfNull(request.ForwardedTypes);
        using var original = new MemoryStream(request.Implementation, writable: false);
        using var platformImage = new MemoryStream(request.Platform, writable: false);
        using var view = AssemblyDefinition.ReadAssembly(original);
        using var platform = AssemblyDefinition.ReadAssembly(platformImage);
        var module = view.MainModule;
        if (module.Assembly.Name.Name == platform.Name.Name)
            throw new ArgumentException("A framework reference view cannot forward to its own assembly identity.");
        var definitions = module.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var platformTypes = platform.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var scope = new FrameworkContractScope(view.Name, platform.Name,
            request.ForwardedTypes.ToFrozenSet(StringComparer.Ordinal), platformTypes.Keys.ToFrozenSet(StringComparer.Ordinal));
        var forwards = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        foreach (var name in request.ForwardedTypes.Order(StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(name, out var source) || !platformTypes.TryGetValue(name, out var destination))
                throw new ArgumentException($"Shared framework type '{name}' must exist in both inputs.");
            var mismatch = checker.Check(new(source, destination, platformTypes, scope));
            if (mismatch != FrameworkTypeMismatch.None)
                throw new ArgumentException($"Framework type '{source.FullName}' has incompatible {mismatch} contracts.");
            forwards.Add(name, module.ImportReference(destination));
        }
        // Removing a declaring type must not silently lose an unmapped nested API.
        foreach (var source in definitions.Values)
            if (source.DeclaringType is { } parent && forwards.ContainsKey(parent.FullName) &&
                !forwards.ContainsKey(source.FullName) && IsVisible(source))
                throw new ArgumentException($"Nested framework type '{source.FullName}' needs an explicit mapping.");

        var map = new FrameworkReferenceMap(module, forwards);
        RewriteAttributes(view, rewrite, map);
        RewriteAttributes(module, rewrite, map);
        foreach (var type in definitions.Values.Where(type => !forwards.ContainsKey(type.FullName)))
        {
            RewriteAttributes(type, rewrite, map);
            if (type.BaseType is not null) type.BaseType = rewrite.Rewrite(type.BaseType, map);
            foreach (var implementation in type.Interfaces)
            {
                RewriteAttributes(implementation, rewrite, map);
                implementation.InterfaceType = rewrite.Rewrite(implementation.InterfaceType, map);
            }
            RewriteParameters(type.GenericParameters, rewrite, map);
            foreach (var field in type.Fields)
            {
                RewriteAttributes(field, rewrite, map);
                field.FieldType = rewrite.Rewrite(field.FieldType, map);
            }
            foreach (var property in type.Properties)
            {
                RewriteAttributes(property, rewrite, map);
                property.PropertyType = rewrite.Rewrite(property.PropertyType, map);
                foreach (var parameter in property.Parameters)
                    parameter.ParameterType = rewrite.Rewrite(parameter.ParameterType, map);
            }
            foreach (var @event in type.Events)
            {
                RewriteAttributes(@event, rewrite, map);
                @event.EventType = rewrite.Rewrite(@event.EventType, map);
            }
            foreach (var method in type.Methods)
            {
                RewriteAttributes(method, rewrite, map);
                RewriteAttributes(method.MethodReturnType, rewrite, map);
                method.ReturnType = rewrite.Rewrite(method.ReturnType, map);
                foreach (var parameter in method.Parameters)
                {
                    RewriteAttributes(parameter, rewrite, map);
                    parameter.ParameterType = rewrite.Rewrite(parameter.ParameterType, map);
                }
                RewriteParameters(method.GenericParameters, rewrite, map);
                for (var index = 0; index < method.Overrides.Count; index++)
                    method.Overrides[index] = rewrite.Rewrite(method.Overrides[index], map);
                // A reference view has declarations, never executable code. Runtime
                // implementation flags leave ordinary nonabstract declarations intact.
                method.Body = null;
                if (!method.IsAbstract && !method.IsPInvokeImpl)
                    method.ImplAttributes = (method.ImplAttributes & ~MethodImplAttributes.CodeTypeMask) |
                        MethodImplAttributes.Runtime;
            }
        }

        var exports = new Dictionary<string, ExportedType>(StringComparer.Ordinal);
        foreach (var (name, destination) in forwards.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var source = definitions[name];
            var export = new ExportedType(source.Namespace, source.Name, module, destination.Scope)
            {
                Attributes = source.Attributes & TypeAttributes.VisibilityMask,
                IsForwarder = true,
            };
            if (source.DeclaringType is { } parent)
            {
                if (!exports.TryGetValue(parent.FullName, out var enclosing))
                    throw new ArgumentException("A nested forwarder requires its declaring forwarder.");
                export.DeclaringType = enclosing;
            }
            exports.Add(name, export);
            module.ExportedTypes.Add(export);
        }
        foreach (var source in definitions.Values.Where(type => forwards.ContainsKey(type.FullName)))
        {
            if (source.DeclaringType is { } parent) parent.NestedTypes.Remove(source);
            else module.Types.Remove(source);
        }
        module.Attributes &= ~ModuleAttributes.StrongNameSigned;
        using var output = new MemoryStream();
        module.Write(output, new WriterParameters { DeterministicMvid = true, Timestamp = 0 });
        return output.ToArray();
    }

    private static bool IsVisible(TypeDefinition type) => type.IsPublic ||
        (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) &&
        type.DeclaringType is { } parent && IsVisible(parent);

    private static void RewriteParameters(IEnumerable<GenericParameter> parameters,
        IFrameworkReferenceTypeRewriter rewrite, FrameworkReferenceMap map)
    {
        foreach (var parameter in parameters)
        {
            RewriteAttributes(parameter, rewrite, map);
            foreach (var constraint in parameter.Constraints)
            {
                RewriteAttributes(constraint, rewrite, map);
                constraint.ConstraintType = rewrite.Rewrite(constraint.ConstraintType, map);
            }
        }
    }

    private static void RewriteAttributes(ICustomAttributeProvider owner,
        IFrameworkReferenceTypeRewriter rewrite, FrameworkReferenceMap map)
    {
        for (var index = 0; index < owner.CustomAttributes.Count; index++)
        {
            var attribute = owner.CustomAttributes[index];
            // Preserve the serialized argument contract without resolving external
            // enums. Assembly-qualified type names in the blob remain valid through
            // the same forwarders. Constructor tokens must follow removed definitions.
            owner.CustomAttributes[index] = new CustomAttribute(
                rewrite.Rewrite(attribute.Constructor, map), attribute.GetBlob());
        }
    }
}
