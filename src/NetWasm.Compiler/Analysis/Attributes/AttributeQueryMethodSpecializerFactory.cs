using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// Abstract Factory: compose bounded attribute lowering for one compilation.
// Metadata selection, provenance, construction and rewriting stay in their actors.
internal sealed class AttributeQueryMethodSpecializerFactory : IAttributeQueryMethodSpecializerFactory
{
    public IMethodSpecializer Create(
        IMethodSpecializer specializer,
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fieldRepository,
        IMethodRepository methodRepository,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver typeIdentities,
        IMethodInstanceResolver methodInstances,
        ITypeRelationshipClassifier relationships,
        IBaseTypeResolver baseTypes,
        ICalledMethodResolver calls,
        ITypeOperandResolver types,
        ISymbolFormatter symbols)
    {
        if (!metadata.Types.Any(type => type.FullName == "System.Attribute")) return specializer;

        var attributeType = typeFinder.FindType("System.Attribute");
        var typeType = typeFinder.FindType("System.Type");
        var memberType = typeFinder.FindType("System.Reflection.MemberInfo");
        var queryOwners = metadata.Types
            .Where(type => type.Key.Assembly == attributeType.Key.Assembly && type.FullName is
                "System.Type" or "System.Reflection.MemberInfo" or "System.Reflection.ICustomAttributeProvider" or
                "System.Attribute" or "System.Reflection.CustomAttributeExtensions")
            .ToImmutableDictionary(type => type.Key, type => type.FullName switch
            {
                "System.Attribute" => AttributeQueryApiKind.AttributeStatic,
                "System.Reflection.CustomAttributeExtensions" => AttributeQueryApiKind.Extension,
                _ => AttributeQueryApiKind.Instance,
            });
        var nullable = typeFinder.FindType("System.Nullable");
        var nullableDefinition = typeIdentities.GetTypeIdentity(typeFinder.FindType("System.Nullable`1").Key);
        var unwrapMethods = nullable.Methods.Select(methodRepository.GetMethod)
            .Where(method => method.Name == "GetUnderlyingType" && method.IsStatic)
            .Select(method => method.Key).ToImmutableHashSet();
        var ambiguity = typeFinder.FindType("System.Reflection.AmbiguousMatchException").Methods
            .Select(methodRepository.GetMethod)
            .Single(method => method.Name == ".ctor" && !method.IsStatic && method.Signature.ParameterTypes.IsEmpty);
        var synthesizedAttributes = metadata.Types
            .Where(type => type.Key.Assembly == attributeType.Key.Assembly && type.FullName is
                "System.SerializableAttribute" or "System.Runtime.InteropServices.ComImportAttribute" or
                "System.Runtime.InteropServices.StructLayoutAttribute")
            .Select(type => typeIdentities.GetTypeIdentity(type.Key)).ToImmutableHashSet();
        var attributeValues = new CustomAttributeValueDecoderFactory().Create(metadata,
            typeDefinitions, typeIdentities, methodInstances, typeType.Key);
        var attributeDescriptors = new CustomAttributeDescriptorReaderFactory().Create(metadata,
            typeDefinitions, typeIdentities);
        var attributeMatches = new AttributeMatchSelector(attributeDescriptors,
            new AttributeUsageReader(attributeDescriptors, attributeValues,
                typeIdentities.GetTypeIdentity(typeFinder.FindType("System.AttributeUsageAttribute").Key)),
            relationships, baseTypes);
        var setterDelegate = typeFinder.FindType("System.Action`1").Methods
            .Select(methodRepository.GetMethod).Single(method => method.Name == ".ctor");
        var setterInvoke = typeFinder.FindType("System.Runtime.CompilerServices.RuntimeCustomAttributes").Methods
            .Select(methodRepository.GetMethod).Single(method => method.Name == "ApplySetter");
        return new AttributeQueryMethodSpecializer(specializer, new AttributeQueryLowerer(
            calls,
            new AttributeQueryClassifier(queryOwners, typeIdentities.GetTypeIdentity(memberType.Key),
                typeIdentities.GetTypeIdentity(typeType.Key)),
            new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator())
                .Create(typeRepository, fieldRepository, methodRepository),
            new AttributeProvenanceAnalyzer(calls, types,
                new ReadonlyTypeFieldValueResolverFactory().Create(metadata, methodInstances,
                    typeDefinitions, typeRepository, typeType.Key),
                unwrapMethods, nullableDefinition),
            new AttributeQueryResolver(typeDefinitions, relationships, typeIdentities.GetTypeIdentity(attributeType.Key),
                synthesizedAttributes),
            new AttributeQueryPlanner(attributeMatches, new AttributeConstructionPlanner(methodInstances,
                attributeValues, new CustomAttributeMemberResolverFactory().Create(metadata, typeDefinitions,
                    typeIdentities, typeRepository, fieldRepository, methodRepository, methodInstances),
                new AttributeArgumentDecoder(typeIdentities.GetTypeIdentity(typeType.Key)), baseTypes)),
            new AttributeQueryCilBuilder(new AttributeConstructionCilBuilder(
                new AttributeSetterBindingResolver(methodInstances, setterDelegate.Key, setterInvoke.Key))),
            new AttributeQueryBodyRewriter(),
            symbols,
            methodInstances.ResolveMethodInstance(ambiguity.Key.Assembly, ambiguity.Key.MetadataToken,
                ambiguity.Name, 0)));
    }
}
