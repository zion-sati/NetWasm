using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ClosedGenericConstraintValidatorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ChecksSubstitutedMethodAndTypeArgumentsUsingSharedRelationships(bool type, bool accepted)
    {
        var owner = new EntityKey(new("Library"), type ? 0x02000001 : 0x06000001);
        var argument = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var required = CliTypeIdentity.GenericParameter(!type, 0);
        var reader = new Reader([new(GenericParameterAttributes.ReferenceTypeConstraint, [required])]);
        var specials = new Specials();
        var relationships = new Relationships(accepted);
        var definition = new TypeDefinitionModel(new(owner.Assembly, 0x02000001), "", "Box`1", false, [], []) { GenericArity = 1 };
        var validator = Assert.IsAssignableFrom<IClosedGenericConstraintValidator>(new ClosedGenericConstraintValidator(
            reader, specials, relationships, new Definitions(definition)));
        var closedType = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.FromDefinition(definition), [argument]);
        var methodDefinition = new MethodDefinitionModel(owner, definition.Key, "Read", true, new(CliValueKind.I4, []), 0) { GenericArity = 1 };
        var method = new MethodInstanceModel(methodDefinition,
            CliTypeIdentity.Named(owner.Assembly, "", "Api", false), [argument], methodDefinition.Signature);
        Action validate = type ? () => validator.Validate(closedType) : () => validator.Validate(method);
        if (accepted)
        {
            validate();
            if (type) validate(); // Successful type checks are shared within a compilation.
        }
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution, Assert.Throws<CompilerException>(validate).Diagnostic.Code);
        Assert.Equal(owner, Assert.Single(reader.Owners));
        Assert.Equal(argument, Assert.Single(specials.Requests).Argument);
        Assert.Equal((argument, argument), Assert.Single(relationships.Calls));
    }

    [Fact]
    public void DefersOpenDescriptionsWithoutReadingConstraints()
    {
        var reader = new Reader([]);
        var validator = Assert.IsAssignableFrom<IClosedGenericConstraintValidator>(new ClosedGenericConstraintValidator(
            reader, new Specials(), new Relationships(true), new Definitions(null!)));
        validator.Validate(CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(new("Library"), "", "Box`1", false),
            [CliTypeIdentity.GenericParameter(false, 0)]));
        validator.Validate(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        // A method of an open type, or with an open method argument, is deferred as well.
        var open = CliTypeIdentity.GenericParameter(true, 0);
        var definition = new MethodDefinitionModel(new(new("Library"), 0x06000001), new(new("Library"), 0x02000001), "Read",
            true, new(CliValueKind.I4, []), 0)
        { GenericArity = 1 };
        validator.Validate(new MethodInstanceModel(definition, CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new("Library"), "", "Box`1", false), [CliTypeIdentity.GenericParameter(false, 0)]),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)], definition.Signature));
        validator.Validate(new MethodInstanceModel(definition, CliTypeIdentity.Named(new("Library"), "", "Api", false),
            [open], definition.Signature));
        Assert.Empty(reader.Owners);
        Assert.Throws<ArgumentNullException>(() => validator.Validate((CliTypeIdentity)null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate((MethodInstanceModel)null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsArityAndUnsubstitutedConstraintDeclarations(bool unresolved)
    {
        var owner = new EntityKey(new("Library"), 0x06000001);
        var parameters = unresolved
            ? ImmutableArray.Create(new GenericParameterConstraintModel(0, [CliTypeIdentity.GenericParameter(true, 1)]))
            : ImmutableArray<GenericParameterConstraintModel>.Empty;
        var validator = Assert.IsAssignableFrom<IClosedGenericConstraintValidator>(new ClosedGenericConstraintValidator(
            new Reader(parameters), new Specials(), new Relationships(true), new Definitions(null!)));
        var definition = new MethodDefinitionModel(owner, new(owner.Assembly, 0x02000001), "Read", true,
            new(CliValueKind.I4, []), 0)
        { GenericArity = 1 };
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(owner.Assembly, "", "Api", false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)], definition.Signature);
        Assert.Equal(DiagnosticCode.AssemblyResolution,
            Assert.Throws<CompilerException>(() => validator.Validate(method)).Diagnostic.Code);
    }

    private sealed class Reader(ImmutableArray<GenericParameterConstraintModel> parameters) : IGenericParameterConstraintReader
    {
        public List<EntityKey> Owners { get; } = [];
        public ImmutableArray<GenericParameterConstraintModel> Read(EntityKey owner) { Owners.Add(owner); return parameters; }
    }
    private sealed class Specials : IGenericArgumentSpecialConstraintValidator
    {
        public List<GenericArgumentSpecialConstraintRequest> Requests { get; } = [];
        public void Validate(GenericArgumentSpecialConstraintRequest request) => Requests.Add(request);
    }
    private sealed class Relationships(bool accepted) : ITypeRelationshipClassifier
    {
        public List<(CliTypeIdentity, CliTypeIdentity)> Calls { get; } = [];
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Calls.Add((candidate, target));
            return new(accepted ? TypeRelationshipCharacteristics.Hierarchy : TypeRelationshipCharacteristics.None);
        }
    }
    private sealed class Definitions(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => definition;
    }
}
