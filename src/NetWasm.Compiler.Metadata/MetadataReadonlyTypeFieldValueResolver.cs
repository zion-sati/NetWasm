using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata;

// Adapter: prove the field-initializer-before-base-call shape in its defining
// assembly, after excluding accessor exposure throughout the metadata snapshot.
internal sealed class MetadataReadonlyTypeFieldValueResolver(
    ImmutableDictionary<AssemblyIdentity, ManagedAssembly> assemblies,
    IMethodInstanceResolver methods,
    IMetadataTypeSignatureResolver signatures,
    IRawCilInstructionReader instructions,
    IUnsafeAccessorFieldExposureClassifier exposures,
    EntityKey typeDefinition) : IReadonlyTypeFieldValueResolver
{
    private readonly ConcurrentDictionary<EntityKey, Lazy<ImmutableArray<int>?>> _initializers = new();

    public CliTypeIdentity? Resolve(EntityKey field)
    {
        // Ordinary field operands carry their definition key. Constructed owners
        // already carry FieldInstanceModel; both must use the same exposure proof.
        var assembly = assemblies[field.Assembly];
        var definition = assembly.Fields[field.MetadataToken];
        var owner = assembly.Types[definition.DeclaringType.MetadataToken];
        return Resolve(new FieldInstanceModel(definition, CliTypeIdentity.FromDefinition(owner), definition.SignatureType));
    }

    public CliTypeIdentity? Resolve(FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Definition.IsStatic || !field.Definition.IsInitOnly || field.DeclaringType.IsValueType ||
            field.FieldType.Assembly is not { } fieldAssembly ||
            fieldAssembly != typeDefinition.Assembly || field.FieldType.FullName != "System.Type")
        {
            return null;
        }
        var assembly = assemblies[field.Definition.Key.Assembly];
        var metadataField = assembly.Reader.GetFieldDefinition(
            (FieldDefinitionHandle)MetadataTokens.EntityHandle(field.Definition.Key.MetadataToken));
        if ((metadataField.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Private)
        {
            return null;
        }
        var tokens = _initializers.GetOrAdd(field.Definition.Key,
            _ => new Lazy<ImmutableArray<int>?>(() => Prove(assembly, field))).Value;
        if (tokens is null)
        {
            return null;
        }
        var context = new CliGenericContext(field.DeclaringType.TypeArguments, []);
        CliTypeIdentity? result = null;
        foreach (var token in tokens.Value)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind is not (HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification))
            {
                return null;
            }
            var identity = signatures.Resolve(assembly.Metadata, token, context);
            if (result is not null && !result.Equals(identity))
            {
                return null;
            }
            result = identity;
        }
        return result;
    }

    private ImmutableArray<int>? Prove(ManagedAssembly assembly, FieldInstanceModel field)
    {
        foreach (var candidate in assemblies.Values)
        {
            foreach (var method in candidate.Methods.Values)
            {
                if (exposures.Classify(method, field))
                {
                    return null;
                }
            }
        }
        var tokens = ImmutableArray.CreateBuilder<int>();
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader,
            assembly.Metadata.AssemblyIdentityAliases);
        foreach (var method in assembly.Methods.Values)
        {
            if (!method.HasBody)
            {
                if (method.DeclaringType == field.Definition.DeclaringType && method.Name == ".ctor" && !method.IsStatic)
                {
                    return null;
                }
                continue;
            }
            ImmutableArray<RawCilInstruction> body;
            MethodBodyBlock methodBody;
            try
            {
                methodBody = assembly.PortableExecutableReader.GetMethodBody(method.RelativeVirtualAddress);
                body = instructions.Read(methodBody.GetILBytes()!);
            }
            catch (BadImageFormatException)
            {
                // An unreadable unrelated body prevents this proof, but must not
                // be decoded as a reachable NetWasm body or silently ignored.
                return null;
            }
            var initializerOffset = -1;
            if (method.DeclaringType == field.Definition.DeclaringType && method.Name == ".ctor" && !method.IsStatic)
            {
                var prefix = body.Where(instruction => instruction.OpCode != OpCodes.Nop).Take(4).ToArray();
                if (prefix.Length != 4 || prefix[0].OpCode != OpCodes.Ldarg_0 ||
                    prefix[1].OpCode != OpCodes.Ldtoken || prefix[2].OpCode != OpCodes.Call ||
                    prefix[3].OpCode != OpCodes.Stfld || !ReferencesField(prefix[3].Token!.Value))
                {
                    return null;
                }
                initializerOffset = prefix[3].Offset;
                // A later edge can revisit the same stfld with a different
                // value, despite there being only one store instruction. A
                // protected prefix can instead catch initialization failure
                // and complete construction with the field still null. The
                // ordinary C# field-initializer prefix has neither shape;
                // branches and handlers entirely after it remain eligible.
                if (body.Any(instruction => instruction.BranchTargets.Any(target => target <= initializerOffset)) ||
                    methodBody.ExceptionRegions.Any(region =>
                        region.TryOffset <= initializerOffset || region.HandlerOffset <= initializerOffset ||
                        region.Kind == ExceptionRegionKind.Filter && region.FilterOffset <= initializerOffset))
                {
                    return null;
                }
                var materializer = methods.ResolveMethodInstance(assembly.Identity, prefix[2].Token!.Value,
                    method.Name, prefix[2].Offset);
                if (materializer.Definition.DeclaringType != typeDefinition ||
                    materializer.Definition.Name != "GetTypeFromHandle" || !materializer.Definition.IsStatic ||
                    materializer.Signature.ParameterTypes.Length != 1)
                {
                    return null;
                }
                tokens.Add(prefix[1].Token!.Value);
            }
            foreach (var instruction in body)
            {
                if (instruction.OpCode.OperandType != OperandType.InlineField || !ReferencesField(instruction.Token!.Value))
                {
                    continue;
                }
                if (instruction.OpCode == OpCodes.Ldflda || instruction.OpCode == OpCodes.Ldsflda ||
                    instruction.OpCode == OpCodes.Stsfld ||
                    instruction.OpCode == OpCodes.Stfld && instruction.Offset != initializerOffset)
                {
                    return null;
                }
            }
        }
        return tokens.Count == 0 ? null : tokens.ToImmutable();

        bool ReferencesField(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                return token == field.Definition.Key.MetadataToken;
            }
            if (handle.Kind != HandleKind.MemberReference)
            {
                return false;
            }
            var member = assembly.Reader.GetMemberReference((MemberReferenceHandle)handle);
            if (assembly.Reader.GetString(member.Name) != field.Definition.Name)
            {
                return false;
            }
            var parent = member.Parent;
            var declaringType = parent.Kind switch
            {
                HandleKind.TypeDefinition => provider.GetTypeFromDefinition(assembly.Reader, (TypeDefinitionHandle)parent, 0),
                HandleKind.TypeReference => provider.GetTypeFromReference(assembly.Reader, (TypeReferenceHandle)parent, 0),
                HandleKind.TypeSpecification => provider.GetTypeFromSpecification(assembly.Reader, null, (TypeSpecificationHandle)parent, 0),
                _ => null,
            };
            var definition = declaringType?.Shape == CliTypeShape.GenericInstantiation
                ? declaringType.ElementType : declaringType;
            var wanted = field.DeclaringType.Shape == CliTypeShape.GenericInstantiation
                ? field.DeclaringType.ElementType! : field.DeclaringType;
            // CLI fields are identified by owner, name and signature. A
            // same-name object field must not prove this Type field's value.
            // Compare declaration signatures so cached proofs stay independent
            // of the first closed owner used to request them.
            return definition is not null && definition.Equals(wanted) &&
                member.GetKind() == MemberReferenceKind.Field &&
                member.DecodeFieldSignature(provider, null).Equals(field.Definition.SignatureType);
        }
    }
}
