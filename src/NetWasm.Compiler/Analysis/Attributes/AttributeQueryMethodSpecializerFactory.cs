// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// Abstract Factory: compose bounded attribute lowering for one compilation.
// Metadata selection, provenance, construction and rewriting stay in their actors.
internal sealed class AttributeQueryMethodSpecializerFactory(
    IMetadataCompilationMaterializationFactory materializations) : IAttributeQueryMethodSpecializerFactory
{
    public AttributeQueryServices Create(
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
        if (!metadata.Types.Any(type => type.FullName == "System.Attribute")) return new(specializer, EmptyProgramReducer.Instance);

        var attributeType = typeFinder.FindType("System.Attribute");
        var typeType = typeFinder.FindType("System.Type");
        var memberType = typeFinder.FindType("System.Reflection.MemberInfo");
        // Assembly attribute queries need CoreLib's Assembly, the RuntimeAssembly.Of<T>() that Assembly.GetExecutingAssembly()
        // becomes, and Type.Assembly.
        var assemblyType = metadata.Types.SingleOrDefault(type => type.Key.Assembly == attributeType.Key.Assembly &&
            type.FullName == "System.Reflection.Assembly");
        var assemblyOf = metadata.Types.SingleOrDefault(type => type.Key.Assembly == attributeType.Key.Assembly &&
                type.FullName == "System.Reflection.RuntimeAssembly")?.Methods.Select(methodRepository.GetMethod)
            .SingleOrDefault(method => method.Name == "Of" && method.IsStatic);
        var typeAssembly = typeType.Methods.Select(methodRepository.GetMethod)
            .SingleOrDefault(method => method.Name == "get_Assembly" && !method.IsStatic);
        var assemblies = assemblyType is null || assemblyOf is null || typeAssembly is null
            ? null
            : new AttributeAssemblySources(typeDefinitions, assemblyOf.Key, typeAssembly.Key);
        var queryOwners = metadata.Types
            .Where(type => type.Key.Assembly == attributeType.Key.Assembly && type.FullName is
                "System.Type" or "System.Reflection.MemberInfo" or "System.Reflection.ICustomAttributeProvider" or
                "System.Attribute" or "System.Reflection.CustomAttributeExtensions" or "System.Reflection.Assembly")
            .Where(type => type.FullName != "System.Reflection.Assembly" || assemblies is not null)
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
        var attributeTypes = new AttributeTypeFilter(relationships, baseTypes, typeDefinitions);
        var attributeMatches = new AttributeMatchSelector(attributeDescriptors,
            new AttributeUsageReader(attributeDescriptors, attributeValues,
                typeIdentities.GetTypeIdentity(typeFinder.FindType("System.AttributeUsageAttribute").Key)),
            attributeTypes, baseTypes);
        var setterDelegate = typeFinder.FindType("System.Action`1").Methods
            .Select(methodRepository.GetMethod).Single(method => method.Name == ".ctor");
        var setterInvoke = typeFinder.FindType("System.Runtime.CompilerServices.RuntimeCustomAttributes").Methods
            .Select(methodRepository.GetMethod).Single(method => method.Name == "ApplySetter");
        var typeIdentity = typeIdentities.GetTypeIdentity(typeType.Key);
        var classifier = new AttributeQueryClassifier(queryOwners, typeIdentities.GetTypeIdentity(memberType.Key),
            typeIdentity, assemblies is null ? null : typeIdentities.GetTypeIdentity(assemblyType!.Key));
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var stacks = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator())
            .Create(typeRepository, fieldRepository, methodRepository);
        var provenance = new AttributeProvenanceAnalyzer(calls, types,
            new ReadonlyTypeFieldValueResolverFactory().Create(metadata, methodInstances,
                typeDefinitions, typeRepository, typeType.Key),
            unwrapMethods, nullableDefinition, assemblies, new AttributeTypeValueClassifier(relationships, typeIdentity),
            fieldRepository);
        var resolver = new AttributeQueryResolver(typeDefinitions, relationships,
            typeIdentities.GetTypeIdentity(attributeType.Key), synthesizedAttributes);
        var planner = new AttributeQueryPlanner(attributeMatches, new AttributeConstructionPlanner(methodInstances,
            attributeValues, new CustomAttributeMemberResolverFactory(materializations).Create(metadata, typeDefinitions,
                typeIdentities, typeRepository, fieldRepository, methodRepository, methodInstances),
            new AttributeArgumentDecoder(typeIdentity), baseTypes),
            new AssemblyAttributeDescriptorReaderFactory().Create(metadata, typeDefinitions, typeIdentities), attributeTypes);
        var fragments = new AttributeQueryCilBuilder(new AttributeConstructionCilBuilder(
            new AttributeSetterBindingResolver(methodInstances, setterDelegate.Key, setterInvoke.Key)));
        var bodies = new AttributeQueryBodyRewriter();
        var nullReference = Constructor("System.NullReferenceException", 0);
        var argumentNull = Constructor("System.ArgumentNullException", 1);
        var ambiguityConstructor = methodInstances.ResolveMethodInstance(ambiguity.Key.Assembly, ambiguity.Key.MetadataToken,
            ambiguity.Name, 0);
        return new(
            new AttributeQueryMethodSpecializer(specializer, new AttributeQueryLowerer(calls, classifier, graphs, stacks,
                provenance, resolver, planner, fragments, bodies, symbols, ambiguityConstructor)),
            new RuntimeTypeAttributeQueryReducer(calls, classifier, graphs, stacks, provenance,
                new RuntimeTypeAttributeQueryResolver(resolver, typeIdentities.GetTypeIdentity(typeFinder.FindType("System.Object").Key)),
                new RuntimeTypeAttributeCandidateSelector(planner), new RuntimeTypeAttributeDispatchBuilder(fragments),
                bodies, ambiguityConstructor, nullReference, argumentNull));

        // The exception constructor a run-time Type query throws through on a null target: NullReferenceException() and
        // ArgumentNullException(string paramName) are each their type's only constructor of that arity.
        MethodInstanceModel Constructor(string type, int parameters)
        {
            var constructor = typeFinder.FindType(type).Methods.Select(methodRepository.GetMethod)
                .Single(method => method.Name == ".ctor" && !method.IsStatic && method.Signature.ParameterTypes.Length == parameters);
            return methodInstances.ResolveMethodInstance(constructor.Key.Assembly, constructor.Key.MetadataToken,
                constructor.Name, 0);
        }
    }
}
