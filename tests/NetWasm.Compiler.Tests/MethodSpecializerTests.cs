using NetWasm.Compiler.Analysis;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests;

public sealed class MethodSpecializerTests
{
    [Fact]
    public void SpecializerRejectsMissingRewriteRules()
    {
        Assert.Throws<ArgumentNullException>(() => new MethodSpecializer(null!));
    }

    [Fact]
    public void SpecializerRewritesInt32LocalInitializationThroughItsContract()
    {
        var body = Body(
            new CilInstruction(
                0,
                1,
                CilOperation.LoadLocalAddress,
                new CilOperand.Index(0)),
            new CilInstruction(
                1,
                2,
                CilOperation.InitializeObject,
                new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(
                    CliValueKind.I4))),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()));

        var rewritten = Specialize(
            new MethodSpecializer(
            [
                new Int32LocalInitializerRewriter(),
            ]),
            body);

        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.ConstantI4(0), rewritten.Instructions[0].Operand);
        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[1].Operand);
    }

    [Fact]
    public void EpilogueRuleRewritesBranchToDirectReturnThroughItsContract()
    {
        var body = Body(
            new CilInstruction(
                0,
                1,
                CilOperation.Branch,
                new CilOperand.BranchTarget(2)),
            new CilInstruction(1, 2, CilOperation.Nop, new CilOperand.None()),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()));

        var rewritten = Rewrite(
            new SharedReturnEpilogueRewriter(),
            body);

        Assert.Equal(CilOperation.Return, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.None(), rewritten.Instructions[0].Operand);
    }

    [Fact]
    public void IntrinsicRuleRewritesTypeHandleCallsThroughItsContract()
    {
        var target = Target("GetTypeFromHandle", 10, "System.Type");
        var rewritten = Rewrite(
            new IntrinsicCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Type")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.MaterializeType, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void InterlockedRuleRewritesClosedCompareExchangeThroughItsContract()
    {
        var valueType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var target = Target(
            "CompareExchange",
            11,
            "System.Threading.Interlocked",
            [valueType],
            genericArity: 1);
        var rewritten = Rewrite(
            new InterlockedCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Threading.Interlocked")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.CompareExchange, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.TypeIdentity(valueType), rewritten.Instructions[0].Operand);
    }

    [Fact]
    public void InterlockedRuleLeavesNonGenericCompareExchangeAsOrdinaryCil()
    {
        var target = Target("CompareExchange", 12, "System.Threading.Interlocked");
        var body = Body(new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.Entity(target.Definition.Key)));

        var rewritten = Rewrite(
            new InterlockedCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Threading.Interlocked")),
            body);

        Assert.Same(body, rewritten);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void ActivatorRuleRewritesValueTypeCreationThroughItsContract()
    {
        var createdType = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Fixtures",
            "Value",
            true);
        var target = Target("CreateInstance", 13, "System.Activator", [createdType], genericArity: 1);
        var rewritten = Rewrite(
            new ActivatorCallRewriter(
                new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                    new EntityKey(new AssemblyIdentity("in-memory"), 20),
                    "Fixtures",
                    "Value",
                    true,
                    [],
                    [])),
                new UnusedMethodRepository(),
                new UnusedMethodInstanceResolver(),
                new FixedSymbolFormatter("System.Activator")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(target))));

        Assert.Equal(CilOperation.DefaultValue, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.TypeIdentity(createdType), rewritten.Instructions[0].Operand);
    }

    [Fact]
    public void ActivatorRuleRejectsReferenceTypesWithoutConstructors()
    {
        var createdType = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Fixtures",
            "Reference",
            false);
        var target = Target("CreateInstance", 122, "System.Activator", [createdType], genericArity: 1);

        var error = Assert.Throws<CompilerException>(() => Rewrite(
            new ActivatorCallRewriter(
                new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                    new EntityKey(new AssemblyIdentity("in-memory"), 123),
                    "Fixtures",
                    "Reference",
                    false,
                    [],
                    [])),
                new UnusedMethodRepository(),
                new UnusedMethodInstanceResolver(),
                new FixedSymbolFormatter("System.Activator")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(target)))));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void ActivatorRuleRejectsTypesWithOnlyUnsupportedConstructors()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var createdType = CliTypeIdentity.Named(assembly, "Fixtures", "Reference", false);
        var target = Target("CreateInstance", 127, "System.Activator", [createdType], genericArity: 1);
        var declaringType = new EntityKey(assembly, 128);
        var staticConstructor = new MethodDefinitionModel(
            new EntityKey(assembly, 129),
            declaringType,
            ".ctor",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var parameterizedConstructor = staticConstructor with
        {
            Key = new EntityKey(assembly, 130),
            IsStatic = false,
            Signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
        };

        var error = Assert.Throws<CompilerException>(() => Rewrite(
            new ActivatorCallRewriter(
                new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                    declaringType,
                    "Fixtures",
                    "Reference",
                    false,
                    [],
                    [staticConstructor.Key, parameterizedConstructor.Key])),
                new MapMethodRepository(staticConstructor, parameterizedConstructor),
                new UnusedMethodInstanceResolver(),
                new FixedSymbolFormatter("System.Activator")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(target)))));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void ActivatorRuleConstructsClosedGenericTypesThroughItsResolver()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var genericDefinitionKey = new EntityKey(assembly, 128);
        var constructor = new MethodDefinitionModel(
            new EntityKey(assembly, 129),
            genericDefinitionKey,
            ".ctor",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var genericType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Fixtures", "Box`1", false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var target = Target("CreateInstance", 130, "System.Activator", [genericType], genericArity: 1);
        var rewritten = Rewrite(
            new ActivatorCallRewriter(
                new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                    genericDefinitionKey,
                    "Fixtures",
                    "Box`1",
                    false,
                    [],
                    [constructor.Key])),
                new MapMethodRepository(constructor),
                new FixedMethodInstanceResolver(constructor, genericType),
                new FixedSymbolFormatter("System.Activator")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(target))));

        Assert.Equal(CilOperation.NewObject, rewritten.Instructions[0].Operation);
        var instance = Assert.IsType<CilOperand.MethodInstance>(
            rewritten.Instructions[0].Operand).Value;
        Assert.Equal(constructor, instance.Definition);
        Assert.Equal(genericType.TypeArguments, instance.MethodArguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivatorRuleRejectsMissingOrOpenTypeArguments(bool openArgument)
    {
        var arguments = openArgument
            ? ImmutableArray.Create(CliTypeIdentity.GenericParameter(true, 0))
            : ImmutableArray<CliTypeIdentity>.Empty;
        var target = Target("CreateInstance", 133, "System.Activator", arguments, genericArity: 1);

        var error = Assert.Throws<CompilerException>(() => Rewrite(
            new ActivatorCallRewriter(
                new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                    new EntityKey(new AssemblyIdentity("in-memory"), 134),
                    "Fixtures",
                    "Value",
                    true,
                    [],
                    [])),
                new UnusedMethodRepository(),
                new UnusedMethodInstanceResolver(),
                new FixedSymbolFormatter("System.Activator")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(target)))));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void InterlockedRuleRejectsOpenGenericArguments()
    {
        var target = Target(
            "CompareExchange",
            124,
            "System.Threading.Interlocked",
            [CliTypeIdentity.GenericParameter(true, 0)],
            genericArity: 1);

        var error = Assert.Throws<CompilerException>(() => Rewrite(
            new InterlockedCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Threading.Interlocked")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.Entity(target.Definition.Key)))));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InitializeArrayRuleRejectsMissingFieldData(int operandKind)
    {
        var target = Target(
            "InitializeArray",
            125,
            "System.Runtime.CompilerServices.RuntimeHelpers");
        var fieldKey = new EntityKey(new AssemblyIdentity("in-memory"), 126);
        var field = new FieldDefinitionModel(
            fieldKey,
            new EntityKey(new AssemblyIdentity("in-memory"), 127),
            "Data",
            CliValueKind.I4,
            true);
        var operand = operandKind switch
        {
            0 => (CilOperand)new CilOperand.Entity(fieldKey),
            1 => new CilOperand.FieldInstance(new FieldInstanceModel(
                field,
                CliTypeIdentity.Named(
                    new AssemblyIdentity("in-memory"),
                    "Fixtures",
                    "DataOwner",
                    isValueType: false),
                field.SignatureType)),
            _ => new CilOperand.None(),
        };
        var error = Assert.Throws<CompilerException>(() => Rewrite(
            new InitializeArrayCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Runtime.CompilerServices.RuntimeHelpers")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.LoadFieldToken,
                    operand),
                new CilInstruction(
                    1,
                    2,
                    CilOperation.Call,
                    new CilOperand.Entity(target.Definition.Key)))));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void IntrinsicRuleRewritesObjectTypeCallsThroughItsContract()
    {
        var target = Target("GetType", 121, "System.Object");
        var rewritten = Rewrite(
            new IntrinsicCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Object")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.CallVirtual,
                new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.GetObjectType, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void IntrinsicRulePreservesConstrainedObjectTypeReceiverContract()
    {
        var target = Target("GetType", 122, "System.Object");
        var valueType = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Test",
            "Value",
            isValueType: true);
        var rewritten = Rewrite(
            new IntrinsicCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Object")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.Constrained,
                    new CilOperand.TypeIdentity(valueType)),
                new CilInstruction(
                    1,
                    2,
                    CilOperation.CallVirtual,
                    new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.Constrained, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.GetObjectType, rewritten.Instructions[1].Operation);
        Assert.Equal(
            new CilOperand.TypeIdentity(valueType),
            rewritten.Instructions[1].Operand);
    }

    [Fact]
    public void IntrinsicRuleLeavesUnresolvedCallsUnchanged()
    {
        var rewritten = Rewrite(
            new IntrinsicCallRewriter(
                new FixedCalledMethodResolver(null!),
                new FixedSymbolFormatter("System.Type")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.None())));

        Assert.Equal(CilOperation.Call, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void IntrinsicRuleLeavesResolvedNonIntrinsicCallsUnchanged()
    {
        var target = Target("Ordinary", 123, "Test.Service");
        var rewritten = Rewrite(
            new IntrinsicCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("Test.Service")),
            Body(new CilInstruction(
                0,
                1,
                CilOperation.Call,
                new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.Call, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void InitializeArrayRuleLeavesUnresolvedCallsUnchanged()
    {
        var fieldKey = new EntityKey(new AssemblyIdentity("in-memory"), 131);
        var rewritten = Rewrite(
            new InitializeArrayCallRewriter(
                new FixedCalledMethodResolver(null!),
                new FixedSymbolFormatter("System.Runtime.CompilerServices.RuntimeHelpers")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.LoadFieldToken,
                    new CilOperand.Entity(fieldKey)),
                new CilInstruction(1, 2, CilOperation.Call, new CilOperand.None())));

        Assert.Equal(CilOperation.LoadFieldToken, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[1].Operation);
    }

    [Fact]
    public void SharedEpilogueRuleRewritesStoredReturnValues()
    {
        var rewritten = Rewrite(
            new SharedReturnEpilogueRewriter(),
            Body(
                new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                new CilInstruction(1, 2, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(2, 3, CilOperation.Branch,
                    new CilOperand.BranchTarget(4)),
                new CilInstruction(3, 4, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(4, 5, CilOperation.LoadLocal, new CilOperand.Index(0)),
                new CilInstruction(5, 6, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(6, 7, CilOperation.Return, new CilOperand.None())));

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Return, rewritten.Instructions[2].Operation);
    }

    [Fact]
    public void SharedEpilogueRuleSkipsMissingTargetsAndWalksNops()
    {
        var rewritten = Rewrite(
            new SharedReturnEpilogueRewriter(),
            Body(
                new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.Branch,
                    new CilOperand.BranchTarget(99)),
                new CilInstruction(2, 3, CilOperation.Branch,
                    new CilOperand.BranchTarget(4)),
                new CilInstruction(3, 4, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(4, 5, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(5, 6, CilOperation.Nop, new CilOperand.None())));

        Assert.Equal(CilOperation.Branch, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Branch, rewritten.Instructions[2].Operation);
    }

    [Fact]
    public void SharedEpilogueRuleHandlesAPathWithoutPreviousInstructions()
    {
        var rewritten = Rewrite(
            new SharedReturnEpilogueRewriter(),
            Body(
                new CilInstruction(0, 1, CilOperation.Branch,
                    new CilOperand.BranchTarget(2)),
                new CilInstruction(1, 2, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(2, 3, CilOperation.LoadLocal,
                    new CilOperand.Index(0)),
                new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None())));

        Assert.Equal(CilOperation.Branch, rewritten.Instructions[0].Operation);
    }

    [Fact]
    public void InitializeArrayRuleKeepsTheFieldOfItsData()
    {
        var target = Target(
            "InitializeArray",
            14,
            "System.Runtime.CompilerServices.RuntimeHelpers");
        var fieldKey = new EntityKey(new AssemblyIdentity("in-memory"), 30);
        var field = new FieldDefinitionModel(
            fieldKey,
            new EntityKey(new AssemblyIdentity("in-memory"), 31),
            "Data",
            CliValueKind.I4,
            true)
        {
            InitialData = [1, 2, 3],
        };
        var instance = new FieldInstanceModel(
            field,
            CliTypeIdentity.Named(
                new AssemblyIdentity("in-memory"),
                "Fixtures",
                "DataOwner",
                isValueType: false),
            field.SignatureType);
        var operand = new CilOperand.FieldInstance(instance);
        var rewritten = Rewrite(
            new InitializeArrayCallRewriter(
                new FixedCalledMethodResolver(target),
                new FixedSymbolFormatter("System.Runtime.CompilerServices.RuntimeHelpers")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.LoadFieldToken,
                    operand),
                new CilInstruction(
                    1,
                    2,
                    CilOperation.Call,
                    new CilOperand.Entity(target.Definition.Key))));

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.InitializeArrayData, rewritten.Instructions[1].Operation);
        var data = Assert.IsType<CilOperand.ArrayInitializerData>(rewritten.Instructions[1].Operand);
        Assert.Same(instance, data.Field);
        Assert.True(data.Value.AsSpan().SequenceEqual([(byte)1, (byte)2, (byte)3]));
    }

    [Fact]
    public void ConstrainedTargetResolverSelectsExplicitInterfaceImplementation()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var interfaceType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "INumber",
            false);
        var constrainedType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "ExplicitNumber",
            true);
        var declaration = Target("get_One", 35, "Fixtures.INumber");
        var body = Target("Fixtures.INumber.get_One", 36, "Fixtures.ExplicitNumber");
        var mapping = new MethodImplementationInstanceModel(
            body,
            declaration with { DeclaringType = interfaceType });
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 37),
            "Fixtures",
            "ExplicitNumber",
            true,
            [],
            []);
        var request = new ConstrainedCallTargetRequest(
            constrainedType,
            concreteType,
            declaration.Definition,
            declaration.Signature,
            [],
            []);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(),
                new FixedMethodImplementationResolver(mapping)),
            request);

        Assert.Equal(body.Definition, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverPrefersExactExplicitImplementationOverNamedCandidates()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var interfaceType = CliTypeIdentity.Named(assembly, "Fixtures", "INumber", false);
        var constrainedType = CliTypeIdentity.Named(assembly, "Fixtures", "ExplicitNumber", true);
        var declaration = Target("TryConvertToChecked", 135, "Fixtures.INumber");
        var body = Target("Fixtures.INumber.TryConvertToChecked", 136, "Fixtures.ExplicitNumber");
        var named = body.Definition with
        {
            Key = new EntityKey(assembly, 137),
            Name = declaration.Definition.Name,
        };
        var mapping = new MethodImplementationInstanceModel(
            body,
            declaration with { DeclaringType = interfaceType });
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 138),
            "Fixtures",
            "ExplicitNumber",
            true,
            [],
            [named.Key]);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(named),
                new FixedMethodImplementationResolver(mapping)),
            new ConstrainedCallTargetRequest(
                constrainedType,
                concreteType,
                declaration.Definition,
                declaration.Signature,
                [],
                []));

        Assert.Equal(body.Definition, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverUsesCanonicalPrimitiveIdentityForMethodImplementations()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var interfaceType = CliTypeIdentity.Named(assembly, "Fixtures", "INumber", false);
        var declaration = Target("TryConvertToChecked", 145, "Fixtures.INumber");
        var body = Target("Fixtures.INumber.TryConvertToChecked", 146, "System.Int32");
        var mapping = new MethodImplementationInstanceModel(
            body,
            declaration with { DeclaringType = interfaceType });
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 147),
            "System",
            "Int32",
            true,
            [],
            []);
        var implementations = new IdentityMethodImplementationResolver(
            "primitive:i4",
            mapping);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(),
                implementations),
            new ConstrainedCallTargetRequest(
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                concreteType,
                declaration.Definition,
                declaration.Signature,
                [],
                []));

        Assert.Equal(body.Definition, resolved);
        Assert.Equal("primitive:i4", implementations.ObservedType?.CanonicalName);
    }

    [Fact]
    public void ConstrainedTargetResolverRejectsMismatchedMappingsAndGenericArities()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var constrainedType = CliTypeIdentity.Named(assembly, "Fixtures", "Number", true);
        var declaration = Target("Convert", 155, "Fixtures.INumber");
        var mismatchedDeclaration = declaration with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I8),
        };
        var mappedBody = Target("Fixtures.INumber.Convert", 156, "Fixtures.Number");
        var matching = mappedBody.Definition with
        {
            Key = new EntityKey(assembly, 157),
            Name = declaration.Definition.Name,
        };
        var wrongArity = matching with
        {
            Key = new EntityKey(assembly, 158),
            GenericArity = 1,
        };
        var wrongName = matching with
        {
            Key = new EntityKey(assembly, 160),
            Name = "Other",
        };
        var otherDeclaration = Target("Other", 161, "Fixtures.IOther");
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 159),
            "Fixtures",
            "Number",
            true,
            [],
            [wrongName.Key, wrongArity.Key, matching.Key]);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(wrongName, wrongArity, matching),
                new FixedMethodImplementationResolver(
                    new MethodImplementationInstanceModel(
                        mappedBody,
                        mismatchedDeclaration),
                    new MethodImplementationInstanceModel(
                        mappedBody,
                        otherDeclaration))),
            new ConstrainedCallTargetRequest(
                constrainedType,
                concreteType,
                declaration.Definition,
                declaration.Signature,
                [],
                []));

        Assert.Equal(matching, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverUsesStackSignatureWhenMetadataTypesDiffer()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var constrainedType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Value",
            true);
        var declaration = Target("Read", 166, "Fixtures.IReader").Definition;
        var declaredParameter = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "DeclaredReference",
            false);
        var callParameter = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "CallReference",
            false);
        var candidate = declaration with
        {
            Key = new EntityKey(assembly, 167),
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                declaredParameter),
        };
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 168),
            "Fixtures",
            "Value",
            true,
            [],
            [candidate.Key]);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(candidate),
                new EmptyMethodImplementationResolver()),
            new ConstrainedCallTargetRequest(
                constrainedType,
                concreteType,
                declaration,
                MethodSignatureModel.Create(
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                    callParameter),
                [],
                []));

        Assert.Equal(candidate, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverUsesStackSignatureForValueTypeFallback()
    {
        var assembly = new AssemblyIdentity("fallback");
        var declaredParameter = CliTypeIdentity.Named(
            assembly,
            "Fallback",
            "Declared",
            false);
        var callParameter = CliTypeIdentity.Named(
            assembly,
            "Fallback",
            "Call",
            false);
        var declaration = new MethodDefinitionModel(
            new EntityKey(assembly, 170),
            new EntityKey(assembly, 171),
            nameof(object.Equals),
            false,
            MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("System.Int32", CliValueKind.I4),
                callParameter),
            0);
        var fallback = declaration with
        {
            Key = new EntityKey(assembly, 172),
            DeclaringType = new EntityKey(assembly, 173),
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Primitive("System.Int32", CliValueKind.I4),
                declaredParameter),
        };
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 174),
            "Fallback",
            "Value",
            true,
            [],
            []);
        var valueType = new TypeDefinitionModel(
            fallback.DeclaringType,
            "System",
            "ValueType",
            false,
            [],
            [fallback.Key]);
        var resolver = new ConstrainedCallTargetResolver(
            new FixedTypeFinder(valueType),
            new MapMethodRepository(fallback),
            new EmptyMethodImplementationResolver());

        var resolved = Resolve(
            resolver,
            new ConstrainedCallTargetRequest(
                CliTypeIdentity.Named(assembly, "Fallback", "Value", true),
                concreteType,
                declaration,
                declaration.Signature,
                [],
                []));

        Assert.Equal(fallback, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverIncludesSystemEnumImplementations()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var constrainedType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Choice",
            true);
        var declaration = Target("ToString", 38, "System.Object");
        var implementation = declaration.Definition with
        {
            Key = new EntityKey(assembly, 39),
            DeclaringType = new EntityKey(assembly, 40),
        };
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 41),
            "Fixtures",
            "Choice",
            true,
            [],
            [])
        {
            IsEnum = true,
        };
        var enumType = new TypeDefinitionModel(
            implementation.DeclaringType,
            "System",
            "Enum",
            false,
            [],
            [implementation.Key]);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new FixedTypeFinder(enumType),
                new MapMethodRepository(declaration.Definition, implementation),
                new EmptyMethodImplementationResolver()),
            new ConstrainedCallTargetRequest(
                constrainedType,
                concreteType,
                declaration.Definition,
                declaration.Signature,
                [],
                []));

        Assert.Equal(implementation, resolved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstrainedTargetResolverPrefersConcreteOverrideThenValueTypeFallback(
        bool hasConcreteOverride)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.ManagedReference);
        var declaration = new MethodDefinitionModel(
            new EntityKey(assembly, 180),
            new EntityKey(assembly, 181),
            "Equals",
            false,
            signature,
            1)
        {
            IsVirtual = true,
        };
        var fallback = declaration with
        {
            Key = new EntityKey(assembly, 182),
            DeclaringType = new EntityKey(assembly, 183),
        };
        var concrete = declaration with
        {
            Key = new EntityKey(assembly, 184),
            DeclaringType = new EntityKey(assembly, 185),
        };
        var misleadingStatic = concrete with
        {
            Key = new EntityKey(assembly, 186),
            IsStatic = true,
        };
        var concreteType = new TypeDefinitionModel(
            concrete.DeclaringType,
            "Fixtures",
            "Value",
            true,
            [],
            hasConcreteOverride
                ? [concrete.Key, misleadingStatic.Key]
                : []);
        var valueType = new TypeDefinitionModel(
            fallback.DeclaringType,
            "System",
            "ValueType",
            false,
            [],
            [fallback.Key]);
        var resolver = new ConstrainedCallTargetResolver(
            new FixedTypeFinder(valueType),
            new MapMethodRepository(
                declaration,
                fallback,
                concrete,
                misleadingStatic),
            new EmptyMethodImplementationResolver());

        var resolved = Resolve(
            resolver,
            new ConstrainedCallTargetRequest(
                CliTypeIdentity.Named(
                    assembly,
                    "Fixtures",
                    "Value",
                    isValueType: true),
                concreteType,
                declaration,
                signature,
                [],
                []));

        Assert.Equal(hasConcreteOverride ? concrete : fallback, resolved);
    }

    [Fact]
    public void ConstrainedTargetResolverUsesDefaultInterfaceBodyForValueTypes()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaration = Target("Write", 162, "Fixtures.IDefault").Definition with
        {
            IsStatic = false,
            RelativeVirtualAddress = 1,
        };
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 163),
            "Fixtures",
            "Value",
            true,
            [],
            []);

        var resolved = Resolve(
            new ConstrainedCallTargetResolver(
                new UnusedTypeFinder(),
                new MapMethodRepository(),
                new EmptyMethodImplementationResolver()),
            new ConstrainedCallTargetRequest(
                CliTypeIdentity.Named(assembly, "Fixtures", "Value", true),
                concreteType,
                declaration,
                declaration.Signature,
                [],
                []));

        Assert.Equal(declaration, resolved);
    }

    [Fact]
    public void ConstrainedRuleClosesDefaultInterfaceBodyOverItsDeclaringType()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var interfaceDefinition = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "IDefault`1",
            false);
        var character = CliTypeIdentity.Primitive("System.Char", CliValueKind.I4);
        var interfaceType = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [character]);
        var declaringKey = new EntityKey(assembly, 166);
        var declaration = new MethodDefinitionModel(
            new EntityKey(assembly, 167),
            declaringKey,
            "Create",
            true,
            new MethodSignatureModel(
                CliTypeIdentity.GenericParameter(false, 0),
                [CliTypeIdentity.GenericParameter(true, 0)]),
            1)
        {
            GenericArity = 1,
        };
        var call = new MethodInstanceModel(
            declaration,
            interfaceType,
            [character],
            new MethodSignatureModel(character, [character]));
        var concreteType = new TypeDefinitionModel(
            declaringKey,
            "System",
            "Char",
            true,
            [],
            []);
        var methodInstances = new SubstitutingMethodInstanceResolver(
            declaration,
            interfaceDefinition);

        var rewritten = Rewrite(
            CreateConstrainedCallRewriter(
                new UnusedTypeFinder(),
                new FixedTypeDefinitionResolver(concreteType),
                new MapMethodRepository(declaration),
                methodInstances,
                new FixedSymbolFormatter("Fixtures.IDefault`1")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.Constrained,
                    new CilOperand.TypeIdentity(character)),
                new CilInstruction(
                    1,
                    2,
                    CilOperation.Call,
                    new CilOperand.MethodInstance(call))));

        var resolved = Assert.IsType<CilOperand.MethodInstance>(
            rewritten.Instructions[1].Operand).Value;
        Assert.Equal(character, resolved.Signature.ReturnSignatureType);
        Assert.Equal([character], methodInstances.ObservedContext?.TypeArguments);
    }

    [Fact]
    public void ConstrainedTargetResolverRejectsUnimplementedAbstractValueTypeMember()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaration = Target("Write", 164, "Fixtures.IAbstract").Definition with
        {
            IsStatic = false,
        };
        var concreteType = new TypeDefinitionModel(
            new EntityKey(assembly, 165),
            "Fixtures",
            "Value",
            true,
            [],
            []);
        var resolver = new ConstrainedCallTargetResolver(
            new UnusedTypeFinder(),
            new MapMethodRepository(),
            new EmptyMethodImplementationResolver());

        var error = Assert.Throws<CompilerException>(() => Resolve(
            resolver,
            new ConstrainedCallTargetRequest(
                CliTypeIdentity.Named(assembly, "Fixtures", "Value", true),
                concreteType,
                declaration,
                declaration.Signature,
                [],
                [])));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void ConstrainedRuleResolvesSealedCallsThroughItsContract()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 40);
        var implementation = new MethodDefinitionModel(
            new EntityKey(assembly, 41),
            declaringKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);
        var declaration = implementation with
        {
            Key = new EntityKey(assembly, 42),
        };
        var constrainedType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Sealed",
            false);
        var typeDefinition = new TypeDefinitionModel(
            declaringKey,
            "Fixtures",
            "Sealed",
            false,
            [],
            [implementation.Key])
        {
            IsSealed = true,
        };
        var rewritten = Rewrite(
            CreateConstrainedCallRewriter(
                new UnusedTypeFinder(),
                new FixedTypeDefinitionResolver(typeDefinition),
                new MapMethodRepository(declaration, implementation),
                new FixedMethodInstanceResolver(implementation, constrainedType),
                new FixedSymbolFormatter("Fixtures.Sealed")),
            Body(
                new CilInstruction(
                    0,
                    1,
                    CilOperation.Constrained,
                    new CilOperand.TypeIdentity(constrainedType)),
                new CilInstruction(
                    1,
                    2,
                    CilOperation.Call,
                    new CilOperand.Entity(declaration.Key))));

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[1].Operation);
        var resolved = Assert.IsType<CilOperand.MethodInstance>(
            rewritten.Instructions[1].Operand).Value;
        Assert.Equal(implementation, resolved.Definition);
        Assert.False(resolved.IsConstructed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstrainedVirtualSpecializationPreservesReferenceNullSemantics(bool valueType)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var key = new EntityKey(assembly, 40);
        var type = CliTypeIdentity.Named(assembly, "Fixtures", "Sealed", valueType);
        var implementation = new MethodDefinitionModel(new EntityKey(assembly, 41), key,
            "ToString", false, MethodSignatureModel.Create(CliValueKind.ManagedReference), 1);
        var declaration = implementation with { Key = new EntityKey(assembly, 42) };
        var definition = new TypeDefinitionModel(key, "Fixtures", "Sealed", valueType, [], [implementation.Key])
        {
            IsSealed = true,
        };
        var rewritten = Rewrite(CreateConstrainedCallRewriter(new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(definition), new MapMethodRepository(declaration, implementation),
            new FixedMethodInstanceResolver(implementation, type), new FixedSymbolFormatter("Fixtures.Sealed")),
            Body(new CilInstruction(0, 1, CilOperation.Constrained, new CilOperand.TypeIdentity(type)),
                new CilInstruction(1, 2, CilOperation.CallVirtual, new CilOperand.Entity(declaration.Key))));

        // A value type's own method takes the managed pointer, so its call
        // drops the prefix and can be inlined like any call on the address.
        Assert.Equal(valueType ? CilOperation.Nop : CilOperation.Constrained, rewritten.Instructions[0].Operation);
        Assert.Equal(valueType ? CilOperation.Call : CilOperation.CallVirtual, rewritten.Instructions[1].Operation);
        Assert.Equal(implementation,
            Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[1].Operand).Value.Definition);
    }

    // A value type that inherits the method passes its address to a body that
    // expects the object, so the call keeps the prefix that boxes it.
    [Fact]
    public void ConstrainedRuleKeepsThePrefixForAnInheritedValueTypeMethod()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var key = new EntityKey(assembly, 40);
        var type = CliTypeIdentity.Named(assembly, "Fixtures", "Value", true);
        var inherited = new MethodDefinitionModel(new EntityKey(assembly, 51), new EntityKey(assembly, 50),
            "GetHashCode", false, MethodSignatureModel.Create(CliValueKind.I4), 1)
        {
            IsVirtual = true,
        };
        var definition = new TypeDefinitionModel(key, "Fixtures", "Value", true, [], []) { IsSealed = true };
        var valueType = new TypeDefinitionModel(inherited.DeclaringType, "System", "ValueType", false, [], [inherited.Key]);
        var rewritten = Rewrite(CreateConstrainedCallRewriter(new FixedTypeFinder(valueType),
            new FixedTypeDefinitionResolver(definition), new MapMethodRepository(inherited),
            new FixedMethodInstanceResolver(inherited, type), new FixedSymbolFormatter("Fixtures.Base")),
            Body(new CilInstruction(0, 1, CilOperation.Constrained, new CilOperand.TypeIdentity(type)),
                new CilInstruction(1, 2, CilOperation.CallVirtual, new CilOperand.Entity(inherited.Key))));

        Assert.Equal(CilOperation.Constrained, rewritten.Instructions[0].Operation);
        Assert.Equal(inherited,
            Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[1].Operand).Value.Definition);
    }

    [Fact]
    public void ConstrainedRuleLeavesGenericParametersAndOpenReferencesUnchanged()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Target("Run", 140, "Fixtures.Target");
        var openType = CliTypeIdentity.GenericParameter(true, 0);
        var referenceType = CliTypeIdentity.Named(assembly, "Fixtures", "Reference", false);
        var body = Body(
            new CilInstruction(0, 1, CilOperation.Constrained,
                new CilOperand.TypeIdentity(openType)),
            new CilInstruction(1, 2, CilOperation.Call,
                new CilOperand.Entity(method.Definition.Key)));
        var open = Rewrite(CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                new EntityKey(assembly, 141), "Fixtures", "Reference", false, [], [])),
            new MapMethodRepository(method.Definition),
            new UnusedMethodInstanceResolver(),
            new FixedSymbolFormatter("Fixtures.Reference")), body);

        Assert.Equal(CilOperation.Constrained, open.Instructions[0].Operation);

        var unchanged = Rewrite(CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                new EntityKey(assembly, 141), "Fixtures", "Reference", false, [], [])),
            new MapMethodRepository(method.Definition),
            new UnusedMethodInstanceResolver(),
            new FixedSymbolFormatter("Fixtures.Reference")),
            body with
            {
                Instructions =
                [
                    body.Instructions[0] with
                    {
                        Operand = new CilOperand.TypeIdentity(referenceType),
                    },
                    body.Instructions[1],
                ],
            });

        Assert.Equal(CilOperation.Constrained, unchanged.Instructions[0].Operation);
    }

    [Fact]
    public void ConstrainedRuleSubstitutesClosedEnclosingTypeArguments()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 150);
        var implementation = new MethodDefinitionModel(
            new EntityKey(assembly, 151),
            declaringKey,
            "Equals",
            false,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            1);
        var declaration = implementation with
        {
            Key = new EntityKey(assembly, 152),
        };
        var closedType = CliTypeIdentity.Primitive("System.Int32", CliValueKind.I4);
        var definition = new TypeDefinitionModel(
            declaringKey,
            "System",
            "Int32",
            true,
            [],
            [implementation.Key]);
        var openType = CliTypeIdentity.GenericParameter(false, 0);
        var owner = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Fixtures", "Container`1", false),
            [closedType]);
        var body = Body(
            new CilInstruction(0, 1, CilOperation.Constrained,
                new CilOperand.TypeIdentity(openType)),
            new CilInstruction(1, 2, CilOperation.CallVirtual,
                new CilOperand.Entity(declaration.Key)));
        body = body with
        {
            MethodInstance = new MethodInstanceModel(
                body.Method,
                owner,
                [],
                body.Method.Signature),
        };

        var rewritten = Rewrite(
            CreateConstrainedCallRewriter(
                new UnusedTypeFinder(),
                new FixedTypeDefinitionResolver(definition),
                new MapMethodRepository(declaration, implementation),
                new FixedMethodInstanceResolver(implementation, closedType),
                new FixedSymbolFormatter("System.Int32")),
            body);

        // Int32 declares Equals, so the call takes the managed pointer itself.
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[1].Operation);
        Assert.Equal(
            implementation,
            Assert.IsType<CilOperand.MethodInstance>(
                rewritten.Instructions[1].Operand).Value.Definition);
    }

    [Fact]
    public void ConstrainedRuleLeavesConstructedArrayReferencesForRuntimeDispatch()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Target("Run", 145, "Fixtures.Target");
        var elementType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Element",
            false);
        var rewriter = CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new RejectingTypeDefinitionResolver(),
            new MapMethodRepository(method.Definition),
            new UnusedMethodInstanceResolver(),
            new FixedSymbolFormatter("Fixtures.Target"));

        foreach (var constrainedType in new[]
                 {
                     CliTypeIdentity.SzArray(elementType),
                     CliTypeIdentity.Array(elementType, 2),
                 })
        {
            var body = Body(
                new CilInstruction(0, 1, CilOperation.Constrained,
                    new CilOperand.TypeIdentity(constrainedType)),
                new CilInstruction(1, 2, CilOperation.CallVirtual,
                    new CilOperand.Entity(method.Definition.Key)));

            var rewritten = Rewrite(rewriter, body);

            Assert.Same(body, rewritten);
            Assert.Equal(CilOperation.Constrained, rewritten.Instructions[0].Operation);
            Assert.Equal(CilOperation.CallVirtual, rewritten.Instructions[1].Operation);
        }
    }

    [Fact]
    public void ConstrainedRuleIgnoresInstructionsOutsideConstrainedCallPairs()
    {
        var rewriter = CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new RejectingTypeDefinitionResolver(),
            new UnusedMethodRepository(),
            new UnusedMethodInstanceResolver(),
            new FixedSymbolFormatter("Fixtures.Target"));
        var candidates = new[]
        {
            Body(
                new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.Add, new CilOperand.None())),
            Body(
                new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.Call, new CilOperand.None())),
            Body(
                new CilInstruction(0, 1, CilOperation.Constrained,
                    new CilOperand.None()),
                new CilInstruction(1, 2, CilOperation.CallVirtual,
                    new CilOperand.None())),
        };

        foreach (var body in candidates)
        {
            Assert.Same(body, Rewrite(rewriter, body));
        }
    }

    [Fact]
    public void ConstrainedRuleRejectsMalformedOperandsAndUnresolvedValueTargets()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var constrainedType = CliTypeIdentity.Named(assembly, "Fixtures", "Value", true);
        var definition = new TypeDefinitionModel(
            new EntityKey(assembly, 150),
            "Fixtures",
            "Value",
            true,
            [],
            [])
        {
            IsSealed = true,
        };
        var method = Target("Run", 151, "Fixtures.Value");
        var rewriter = CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(definition),
            new MapMethodRepository(method.Definition),
            new UnusedMethodInstanceResolver(),
            new FixedSymbolFormatter("Fixtures.Value"));
        var prefix = new CilInstruction(0, 1, CilOperation.Constrained,
            new CilOperand.TypeIdentity(constrainedType));

        Assert.Throws<InvalidOperationException>(() => Rewrite(rewriter, Body(
            prefix,
            new CilInstruction(1, 2, CilOperation.Call, new CilOperand.None()))));
        var error = Assert.Throws<CompilerException>(() => Rewrite(rewriter, Body(
            prefix,
            new CilInstruction(1, 2, CilOperation.Call,
                new CilOperand.Entity(method.Definition.Key)))));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);

        var first = method.Definition with
        {
            Key = new EntityKey(assembly, 152),
        };
        var second = method.Definition with
        {
            Key = new EntityKey(assembly, 153),
        };
        var multiple = Assert.Throws<CompilerException>(() => Rewrite(
            CreateConstrainedCallRewriter(
                new UnusedTypeFinder(),
                new FixedTypeDefinitionResolver(definition with
                {
                    Methods = [first.Key, second.Key],
                }),
                new MapMethodRepository(first, second, method.Definition),
                new UnusedMethodInstanceResolver(),
                new FixedSymbolFormatter("Fixtures.Value")),
            Body(prefix, new CilInstruction(1, 2, CilOperation.Call,
                new CilOperand.Entity(method.Definition.Key)))));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, multiple.Diagnostic.Code);
    }

    [Fact]
    public void ConstrainedRuleUsesStackSignatureFallbackAndDeclarationForReferenceTypes()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = new EntityKey(assembly, 160);
        var constrainedType = CliTypeIdentity.Named(assembly, "Fixtures", "Reference", false);
        var customValue = CliTypeIdentity.Named(assembly, "Fixtures", "StackEquivalent", true,
            CliValueKind.I4);
        var implementation = new MethodDefinitionModel(
            new EntityKey(assembly, 161),
            declaringType,
            "Run",
            true,
            MethodSignatureModel.Create(customValue),
            1);
        var declaration = implementation with
        {
            Key = new EntityKey(assembly, 162),
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        var typeDefinition = new TypeDefinitionModel(
            declaringType,
            "Fixtures",
            "Reference",
            false,
            [],
            [implementation.Key])
        {
            IsSealed = true,
        };
        var rewriter = CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(typeDefinition),
            new MapMethodRepository(declaration, implementation),
            new FixedMethodInstanceResolver(implementation, constrainedType),
            new FixedSymbolFormatter("Fixtures.Reference"));
        var rewritten = Rewrite(rewriter, Body(
            new CilInstruction(0, 1, CilOperation.Constrained,
                new CilOperand.TypeIdentity(constrainedType)),
            new CilInstruction(1, 2, CilOperation.Call,
                new CilOperand.Entity(declaration.Key))));

        var resolved = Assert.IsType<CilOperand.MethodInstance>(
            rewritten.Instructions[1].Operand).Value;
        Assert.Equal(implementation, resolved.Definition);
        Assert.False(resolved.IsConstructed);

        var noCandidate = Rewrite(CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(typeDefinition with
            {
                Methods = [new EntityKey(assembly, 163)],
            }),
            new MapMethodRepository(
                declaration,
                implementation with
                {
                    Key = new EntityKey(assembly, 163),
                    Signature = MethodSignatureModel.Create(
                        CliValueKind.I4,
                        CliValueKind.I4),
                }),
            new FixedMethodInstanceResolver(declaration, constrainedType),
            new FixedSymbolFormatter("Fixtures.Reference")), Body(
                new CilInstruction(0, 1, CilOperation.Constrained,
                    new CilOperand.TypeIdentity(constrainedType)),
                new CilInstruction(1, 2, CilOperation.Call,
                    new CilOperand.Entity(declaration.Key))));

        var fallback = Assert.IsType<CilOperand.MethodInstance>(
            noCandidate.Instructions[1].Operand).Value;
        Assert.Equal(declaration, fallback.Definition);
        Assert.False(fallback.IsConstructed);
    }

    [Fact]
    public void ConstrainedRuleResolvesGenericLoadFunctionInstances()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var genericDefinition = CliTypeIdentity.Named(assembly, "Fixtures", "Box`1", false);
        var constrainedType = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var declaringType = new EntityKey(assembly, 170);
        var implementation = new MethodDefinitionModel(
            new EntityKey(assembly, 171),
            declaringType,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var declaration = implementation with { Key = new EntityKey(assembly, 172) };
        var declarationInstance = new MethodInstanceModel(
            declaration,
            constrainedType,
            [],
            declaration.Signature);
        var rewritten = Rewrite(CreateConstrainedCallRewriter(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(new TypeDefinitionModel(
                declaringType,
                "Fixtures",
                "Box`1",
                false,
                [],
                [implementation.Key])
            {
                IsSealed = true,
            }),
            new MapMethodRepository(declaration, implementation),
            new FixedMethodInstanceResolver(implementation, constrainedType),
            new FixedSymbolFormatter("Fixtures.Box")), Body(
                new CilInstruction(0, 1, CilOperation.Constrained,
                    new CilOperand.TypeIdentity(constrainedType)),
                new CilInstruction(1, 2, CilOperation.LoadVirtualFunction,
                    new CilOperand.MethodInstance(declarationInstance))));

        Assert.Equal(CilOperation.LoadFunction, rewritten.Instructions[1].Operation);
        var resolved = Assert.IsType<CilOperand.MethodInstance>(rewritten.Instructions[1].Operand).Value;
        Assert.Equal(implementation, resolved.Definition);
        Assert.Equal(constrainedType, resolved.DeclaringType);
    }

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(
            new MethodDefinitionModel(
                new EntityKey(new AssemblyIdentity("in-memory"), 1),
                new EntityKey(new AssemblyIdentity("in-memory"), 2),
                "Run",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                1),
            1,
            [CliValueKind.I4],
            [.. instructions]);

    private static MethodInstanceModel Target(
        string name,
        int token,
        string declaringType,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> arguments = default,
        int genericArity = 0)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, token + 100);
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, token),
            declaringKey,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            0)
        {
            GenericArity = genericArity,
        };
        var separator = declaringType.LastIndexOf('.');
        return new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                assembly,
                declaringType[..separator],
                declaringType[(separator + 1)..],
                false),
            arguments.IsDefault ? [] : arguments,
            definition.Signature);
    }

    private static CilMethodBody Rewrite<TRewriter>(
        TRewriter rewriter,
        CilMethodBody body)
        where TRewriter : IMethodRewriteRule =>
        rewriter.Rewrite(body);

    private static MethodDefinitionModel Resolve<TResolver>(
        TResolver resolver,
        ConstrainedCallTargetRequest request)
        where TResolver : IConstrainedCallTargetResolver =>
        resolver.Resolve(request);

    private static ConstrainedCallRewriter CreateConstrainedCallRewriter(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols) =>
        new(
            types,
            typeDefinitions,
            methods,
            methodInstances,
            symbols,
            new ConstrainedCallTargetResolver(
                types,
                methods,
                new EmptyMethodImplementationResolver()));

    private static CilMethodBody Specialize<TSpecializer>(
        TSpecializer specializer,
        CilMethodBody body)
        where TSpecializer : IMethodSpecializer =>
        specializer.Rewrite(body);

    private sealed class FixedCalledMethodResolver(MethodInstanceModel target) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => target;
    }

    private sealed class FixedSymbolFormatter(string declaringType) : ISymbolFormatter
    {
        public string Format(EntityKey key) => declaringType;

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class FixedTypeDefinitionResolver(TypeDefinitionModel definition) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => definition;
    }

    private sealed class RejectingTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new InvalidOperationException("Constructed types do not name definitions.");
    }

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new InvalidOperationException();
    }

    private sealed class MapMethodRepository(params MethodDefinitionModel[] methods) :
        IMethodRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class UnusedMethodInstanceResolver : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedMethodInstanceResolver(
        MethodDefinitionModel method,
        CliTypeIdentity declaringType) : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            new(
                method,
                declaringType,
                genericContext?.TypeArguments ?? [],
                method.Signature);
    }

    private sealed class SubstitutingMethodInstanceResolver(
        MethodDefinitionModel method,
        CliTypeIdentity declaringType) : IMethodInstanceResolver
    {
        public CliGenericContext? ObservedContext { get; private set; }

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null)
        {
            ObservedContext = genericContext;
            var context = genericContext ?? CliGenericContext.Empty;
            return new(
                method,
                declaringType,
                context.MethodArguments,
                method.Signature.Substitute(
                    context.TypeArguments,
                    context.MethodArguments));
        }
    }

    private sealed class UnusedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => throw new InvalidOperationException();
    }

    private sealed class FixedTypeFinder(TypeDefinitionModel type) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => type;
    }

    private sealed class EmptyMethodImplementationResolver : IMethodImplementationResolver
    {
        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
            CliTypeIdentity type) => [];
    }

    private sealed class FixedMethodImplementationResolver(
        params MethodImplementationInstanceModel[] mappings) :
        IMethodImplementationResolver
    {
        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
            CliTypeIdentity type) => [.. mappings];
    }

    private sealed class IdentityMethodImplementationResolver(
        string expectedCanonicalName,
        params MethodImplementationInstanceModel[] mappings) :
        IMethodImplementationResolver
    {
        public CliTypeIdentity? ObservedType { get; private set; }

        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
            CliTypeIdentity type)
        {
            ObservedType = type;
            return type.CanonicalName == expectedCanonicalName ? [.. mappings] : [];
        }
    }
}
