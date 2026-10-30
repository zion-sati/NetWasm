using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class DefaultComparerCallRewriterTests
{
    [Theory]
    [InlineData(true, "string", false, "StringEqualityComparer")]
    [InlineData(true, "nullable", false, "NullableEqualityComparer`1")]
    [InlineData(true, "enum", false, "EnumEqualityComparer`1")]
    [InlineData(true, "value", true, "GenericEqualityComparer`1")]
    [InlineData(true, "reference", false, "ObjectEqualityComparer`1")]
    [InlineData(false, "nullable", false, "NullableComparer`1")]
    [InlineData(false, "enum", false, "EnumComparer`1")]
    [InlineData(false, "value", true, "GenericComparer`1")]
    [InlineData(false, "reference", false, "ObjectComparer`1")]
    public void SelectsTheClosedComparerAndPreservesCallLocation(bool equality, string kind,
        bool compatible, string expected)
    {
        var fixture = new Fixture(equality, kind, compatible);
        var body = Fixture.Body();

        var rewritten = fixture.Rewriter().Rewrite(body);

        Assert.NotSame(body, rewritten);
        var instruction = Assert.Single(rewritten.Instructions);
        Assert.Equal(CilOperation.NewObject, instruction.Operation);
        Assert.Equal(12, instruction.Offset);
        Assert.Equal(17, instruction.NextOffset);
        var constructor = Assert.IsType<CilOperand.MethodInstance>(instruction.Operand).Value;
        Assert.Equal("System.Collections.Generic." + expected, constructor.DeclaringType.ElementType?.FullName
            ?? constructor.DeclaringType.FullName);
        Assert.Equal(".ctor", constructor.Definition.Name);
        Assert.Equal(kind == "string" ? 0 : 1, constructor.DeclaringType.TypeArguments.Length);
        if (kind != "string")
            Assert.Equal(kind == "nullable" ? Fixture.Integer : fixture.Compared,
                Assert.Single(constructor.DeclaringType.TypeArguments));
        Assert.Equal(1, fixture.ConstructorResolutions);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("owner")]
    [InlineData("unresolved")]
    [InlineData("method")]
    [InlineData("nongeneric")]
    [InlineData("arity")]
    [InlineData("noncall")]
    public void LeavesInapplicableCallsUnchangedWithoutConstructingAComparer(string reason)
    {
        var fixture = new Fixture(true, reason == "open" ? "open" : "reference", false);
        if (reason == "owner") fixture.OwnerName = "Fixture.Other`1";
        if (reason == "unresolved") fixture.Target = null;
        if (reason == "method") fixture.Target = fixture.Target! with
        { Definition = fixture.Target.Definition with { Name = "Other" } };
        if (reason == "nongeneric") fixture.Target = fixture.Target! with
        { DeclaringType = fixture.Target.DeclaringType.ElementType! };
        if (reason == "arity") fixture.Target = fixture.Target! with
        { DeclaringType = CliTypeIdentity.GenericInstantiation(fixture.Target.DeclaringType.ElementType!, [Fixture.Integer, Fixture.Integer]) };
        var body = Fixture.Body(reason == "noncall" ? CilOperation.Nop : CilOperation.Call);

        Assert.Same(body, fixture.Rewriter().Rewrite(body));
        Assert.Equal(0, fixture.ConstructorResolutions);
        Assert.Empty(fixture.TypeLookups);
    }

    private sealed class Fixture : ITypeDefinitionResolver, IMethodRepository, IMethodInstanceResolver,
        ITypeFinder, ICalledMethodResolver, ISymbolFormatter, ITypeRelationshipClassifier, IConstrainedCallTargetResolver
    {
        private static readonly AssemblyIdentity Assembly = new("comparer-tests");
        private static readonly EntityKey Owner = new(Assembly, 0x02000001);
        public static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        private readonly bool _enum;
        private readonly bool _compatible;
        private TypeDefinitionModel? _selected;
        public string OwnerName { get; set; }
        public CliTypeIdentity Compared { get; }
        public MethodInstanceModel? Target { get; set; }
        public List<string> TypeLookups { get; } = [];
        public int ConstructorResolutions { get; private set; }

        public Fixture(bool equality, string kind, bool compatible)
        {
            _enum = kind == "enum";
            _compatible = compatible;
            OwnerName = "System.Collections.Generic." + (equality ? "EqualityComparer`1" : "Comparer`1");
            Compared = kind switch
            {
                "open" => CliTypeIdentity.GenericParameter(false, 0),
                "string" => CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference),
                "nullable" => CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Integer]),
                _ => CliTypeIdentity.Named(Assembly, "Fixture", "Value", kind != "reference"),
            };
            var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
            var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), Owner,
                "CreateDefaultComparer", true, signature, 1);
            Target = new(definition, CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System.Collections.Generic", equality ? "EqualityComparer`1" : "Comparer`1", false),
                [Compared]), [], signature);
        }

        public DefaultComparerCallRewriter Rewriter() => new(
            this, this, this, this, this, this, this, this, new StackValueConsumerFinder(new StackEffectResolver(this)),
            devirtualize: true);
        public static CilMethodBody Body(CilOperation operation = CilOperation.Call) => new(
            new(new(Assembly, 0x06000002), Owner, "Caller", true, MethodSignatureModel.Create(CliValueKind.Void), 1),
            1, [], [new(12, 17, operation, new CilOperand.None())]);
        public MethodInstanceModel? Resolve(CilInstruction instruction) => Target;
        public string Format(EntityKey key) => OwnerName;
        public string Format(MethodDefinitionModel method) => method.Name;
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(Owner, "Fixture", "Value", true, [], []) { IsEnum = _enum };
        // These bodies make no call on a default comparer.
        public MethodDefinitionModel Resolve(ConstrainedCallTargetRequest request) =>
            throw new InvalidOperationException();
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            Assert.Equal(Compared, candidate);
            Assert.Equal(Compared, Assert.Single(target.TypeArguments));
            return new(_compatible ? TypeRelationshipCharacteristics.Hierarchy : TypeRelationshipCharacteristics.None);
        }
        public TypeDefinitionModel FindType(string fullName)
        {
            TypeLookups.Add(fullName);
            var split = fullName.LastIndexOf('.');
            var definition = new TypeDefinitionModel(new(Assembly, 0x02000002), fullName[..split], fullName[(split + 1)..],
                false, [], [new(Assembly, 0x06000003), new(Assembly, 0x06000004), new(Assembly, 0x06000005), new(Assembly, 0x06000006)]);
            if (!fullName.StartsWith("System.I", StringComparison.Ordinal)) _selected = definition;
            return definition;
        }
        public MethodDefinitionModel GetMethod(EntityKey key) => new(key, _selected!.Key,
            key.MetadataToken == 0x06000003 ? "Other" : ".ctor", key.MetadataToken == 0x06000004,
            key.MetadataToken == 0x06000005 ? MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)
                : MethodSignatureModel.Create(CliValueKind.Void), 1);
        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            ConstructorResolutions++;
            var definition = GetMethod(new(source, metadataToken));
            Assert.Equal(0x06000006, metadataToken);
            return new(definition, CliTypeIdentity.FromDefinition(_selected!), [], definition.Signature);
        }
    }
}
