using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using System.Collections.Immutable;

using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Tests;

public sealed class ReachabilityInstructionAnalyzerTests
{
    [Fact]
    public void StaticDataElementRetainsOnlyItsBackingFieldAndElementType()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "EntryPoint",
            isValueType: false);
        var caller = Method(2, "Run", declaringKey);
        var element = CliTypeIdentity.Primitive("u4", CliValueKind.I4);
        var span = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                assembly,
                "System",
                "ReadOnlySpan`1",
                isValueType: true),
            [element]);
        var dataType = CliTypeIdentity.Named(
            assembly,
            "",
            "__StaticArrayInitTypeSize4",
            isValueType: true);
        var fieldDefinition = new FieldDefinitionModel(
            new(assembly, 3),
            new(assembly, 4),
            "Data",
            dataType,
            true)
        {
            InitialData = [1, 0, 0, 0],
        };
        var field = new FieldInstanceModel(
            fieldDefinition,
            declaringType,
            dataType);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new(
            Instance(caller, declaringType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.LoadStaticDataElement,
                    new CilOperand.ReadOnlySpanData(
                        field,
                        span,
                        element,
                        1))])));

        Assert.Equal(field, Assert.Single(result.Fields));
        Assert.Equal(element, Assert.Single(result.ConstructedTypes));
        Assert.DoesNotContain(span, result.ConstructedTypes);
    }

    [Fact]
    public void StaticDataElementRejectsMissingMetadata()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "EntryPoint",
            isValueType: false);
        var caller = Method(2, "Run", declaringKey);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(new(
            Instance(caller, declaringType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.LoadStaticDataElement,
                    new CilOperand.None())]))));
    }

    [Fact]
    public void RvaSpanCreationRejectsMissingMetadata()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "EntryPoint",
            isValueType: false);
        var caller = Method(2, "Run", declaringKey);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(new(
            Instance(caller, declaringType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.CreateReadOnlySpanFromRva,
                    new CilOperand.None())]))));
    }

    [Fact]
    public void AddressTakenNativeCallbackIsReachedWithoutBecomingManagedCallable()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 2);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Callbacks",
            isValueType: false);
        var caller = Method(1, "Run", declaringKey);
        var callback = Method(2, "Callback", declaringKey) with
        {
            NativeCallback = new([], null, false, false),
        };
        var callbackInstance = Instance(callback, declaringType);
        var body = new CilMethodBody(
            caller,
            1,
            [],
            [Instruction(0, CilOperation.LoadFunction,
                new CilOperand.MethodInstance(callbackInstance))]);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(callbackInstance),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new(
            Instance(caller, declaringType),
            body));

        Assert.Equal(callbackInstance, Assert.Single(result.NativeCallbacks));
        Assert.Empty(result.CallableMethods);
        Assert.Equal(callbackInstance, Assert.Single(result.Methods).Method);
        Assert.Equal(CilOperation.Call, Assert.Single(result.Methods).Operation);
        Assert.Equal(callbackInstance, Assert.Single(result.CallSites).Target);
    }

    [Fact]
    public void DirectManagedNativeCallbackInvocationRejects()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 2);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Callbacks",
            isValueType: false);
        var caller = Method(1, "Run", declaringKey);
        var callback = Method(2, "Callback", declaringKey) with
        {
            NativeCallback = new([], null, false, false),
        };
        var callbackInstance = Instance(callback, declaringType);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(callbackInstance),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var exception = Assert.Throws<CompilerException>(() => analyzer.Analyze(new(
            Instance(caller, declaringType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.Call,
                    new CilOperand.MethodInstance(callbackInstance))]))));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
        Assert.Equal(callbackInstance.CanonicalName, exception.Diagnostic.Method);
    }

    [Fact]
    public void AnalyzerDoesNotTreatValueConstructionAsHeapAllocation()
    {
        var assembly = new AssemblyIdentity("Test.Assembly");
        var callerTypeKey = new EntityKey(assembly, 0x02000001);
        var valueTypeKey = new EntityKey(assembly, 0x02000002);
        var callerType = CliTypeIdentity.Named(assembly, "Fixtures", "Caller", isValueType: false);
        var valueType = CliTypeIdentity.Named(assembly, "Fixtures", "Value", isValueType: true);
        var caller = Method(0x06000001, "Run", callerTypeKey);
        var constructor = Method(0x06000002, ".ctor", valueTypeKey);
        var callerInstance = Instance(caller, callerType);
        var constructorInstance = Instance(constructor, valueType);
        var body = new CilMethodBody(
            caller,
            MaxStack: 1,
            Locals: [],
            Instructions: [Instruction(0, CilOperation.NewObject, new CilOperand.None())])
        {
            MethodInstance = callerInstance,
        };
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(constructorInstance),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(callerInstance, body));

        Assert.DoesNotContain(valueType, result.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerReturnsStringRootsInItsFacts()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            1);
        var body = new CilMethodBody(
            method,
            1,
            [],
            [
                new CilInstruction(
                    0,
                    1,
                    CilOperation.LoadString,
                    new CilOperand.UserString("root")),
            ]);
        var stringType = CliTypeIdentity.Named(assembly, "System", "String", false);
        var stringDefinition = new TypeDefinitionModel(
            new EntityKey(assembly, 3),
            "System",
            "String",
            false,
            [],
            []);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new FixedTypeFinder(stringDefinition),
            new FixedTypeIdentityResolver(stringType),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            new MethodInstanceModel(method, CliTypeIdentity.Primitive(
                "object",
                CliValueKind.ManagedReference,
                false), [], method.Signature),
            body));

        Assert.Contains("root", result.Strings);
        Assert.Contains(stringDefinition.Key, result.Types);
        Assert.Contains(stringType, result.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerRootsTheUnderlyingBoxForNullableValues()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(20, "Run", new EntityKey(assembly, 21));
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "System", "Nullable`1", true),
            [underlying]);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, nullable),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.Box, new CilOperand.TypeIdentity(nullable))])));

        Assert.Contains(underlying, result.AllocatedTypes);
        Assert.Contains(underlying, result.RuntimeTypes);
        Assert.DoesNotContain(nullable, result.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerRootsNullableUnboxMembershipWithoutInventingAnAllocation()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(22, "Run", new EntityKey(assembly, 23));
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "System", "Nullable`1", true),
            [underlying]);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(nullable),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly,
                "Fixtures",
                "Owner",
                isValueType: false)),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.UnboxAny,
                    new CilOperand.TypeIdentity(nullable))])));

        Assert.Contains(underlying, result.RuntimeTypes);
        Assert.Contains(underlying, result.ConstructedTypes);
        Assert.DoesNotContain(underlying, result.AllocatedTypes);
        Assert.DoesNotContain(nullable, result.RuntimeTypes);
    }

    [Fact]
    public void AnalyzerTreatsArrayTypeTestsAsPotentialAllocations()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(30, "Run", new EntityKey(assembly, 31));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = CliTypeIdentity.Array(element, 2);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(array),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly, "Fixtures", "Owner", isValueType: false)),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.IsInstance, new CilOperand.Entity(
                    new EntityKey(assembly, 32)))])));

        Assert.Contains(array, result.RuntimeTypes);
        Assert.Contains(array, result.ConstructedTypes);
        Assert.Contains(array, result.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerRootsReferenceUnboxAnyTypeTests()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(30, "Run", new EntityKey(assembly, 31));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = CliTypeIdentity.Array(element, 2);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(array),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly, "Fixtures", "Owner", isValueType: false)),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.UnboxAny, new CilOperand.Entity(
                    new EntityKey(assembly, 32)))])));

        Assert.Contains(array, result.RuntimeTypes);
        Assert.Contains(array, result.ConstructedTypes);
        Assert.Contains(array, result.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerIgnoresValueUnboxAnyTypeTests()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(30, "Run", new EntityKey(assembly, 31));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(element),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly, "Fixtures", "Owner", isValueType: false)),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.UnboxAny, new CilOperand.Entity(
                    new EntityKey(assembly, 32)))])));

        Assert.Empty(result.RuntimeTypes);
        Assert.Empty(result.ConstructedTypes);
        Assert.Empty(result.AllocatedTypes);
    }

    [Theory]
    [InlineData(CilOperation.NewRectangularArray)]
    [InlineData(CilOperation.NewBoundedRectangularArray)]
    public void AnalyzerRootsRectangularArrayElementsAndConstruction(CilOperation operation)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(33, "Run", new EntityKey(assembly, 34));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = CliTypeIdentity.Array(element, 2);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(array),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly, "Fixtures", "Owner", isValueType: false)),
            new CilMethodBody(
                method,
                2,
                [],
                [Instruction(
                    0,
                    operation,
                    new CilOperand.TypeIdentity(array))])));

        Assert.Contains(element, result.RuntimeTypes);
        Assert.Contains(array, result.AllocatedTypes);
        Assert.Contains(array, result.ConstructedTypes);
    }

    [Fact]
    public void AnalyzerRootsTypeTokensResolvedFromEntityOperands()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(34, "Run", new EntityKey(assembly, 35));
        var runtimeType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Template`1",
            isValueType: false);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(runtimeType),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, CliTypeIdentity.Named(
                assembly, "Fixtures", "Owner", isValueType: false)),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.LoadTypeToken, new CilOperand.Entity(
                    new EntityKey(assembly, 36)))])));

        Assert.Contains(runtimeType, result.RuntimeTypes);
        Assert.Contains(runtimeType, result.ConstructedTypes);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public void AnalyzerRootsConstrainedObjectTypeMetadataOnlyWhenPresent()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Owner",
            isValueType: false);
        var constrainedType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var method = Method(35, "Run", new EntityKey(assembly, 36));
        var analyzer = new ReachabilityInstructionAnalyzer(
            new RecordingTypeFinder(),
            new FixedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var constrained = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, declaringType),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.GetObjectType,
                    new CilOperand.TypeIdentity(constrainedType))])));
        var ordinary = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, declaringType),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(
                    0,
                    CilOperation.GetObjectType,
                    new CilOperand.None())])));

        Assert.Contains(constrainedType, constrained.RuntimeTypes);
        Assert.DoesNotContain(constrainedType, ordinary.RuntimeTypes);
        Assert.NotEmpty(constrained.Types);
        Assert.NotEmpty(constrained.AllocatedTypes);
    }

    [Fact]
    public void AnalyzerClassifiesDirectTypeOperandsForArrayCastsAndOtherUses()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = Method(37, "Run", new EntityKey(assembly, 38));
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = CliTypeIdentity.Array(element, 2);
        var ordinary = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Ordinary",
            isValueType: false);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new FixedTypeOperandResolver(array),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, ordinary),
            new CilMethodBody(
                method,
                1,
                [],
                [
                    Instruction(0, CilOperation.CastClass,
                        new CilOperand.TypeIdentity(array)),
                    Instruction(1, CilOperation.IsInstance,
                        new CilOperand.TypeIdentity(ordinary)),
                    Instruction(2, CilOperation.DefaultValue,
                        new CilOperand.TypeIdentity(array)),
                ])));

        Assert.Contains(array, result.AllocatedTypes);
        Assert.Contains(array, result.RuntimeTypes);
        Assert.Contains(ordinary, result.RuntimeTypes);
    }

    [Fact]
    public void AnalyzerCollectsEveryReachabilityInstructionFact()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 100);
        var declaringType = CliTypeIdentity.Named(assembly, "Fixtures", "Target", false);
        var method = Method(1, "Run", declaringKey);
        var delegateConstructor = Method(2, ".ctor", new EntityKey(assembly, 101)) with
        {
            IsVirtual = false,
        };
        var delegateInvoke = Method(3, "Invoke", new EntityKey(assembly, 101));
        var ordinary = Method(4, "Call", declaringKey);
        var virtualMethod = Method(5, "Virtual", declaringKey) with { IsVirtual = true };
        var fieldDefinition = new FieldDefinitionModel(
            new EntityKey(assembly, 200),
            declaringKey,
            "Value",
            CliValueKind.I4,
            false);
        var field = new FieldInstanceModel(fieldDefinition, declaringType,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var elementType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var instructions = new[]
        {
            Instruction(0, CilOperation.NewObject, new CilOperand.MethodInstance(
                Instance(delegateConstructor, declaringType))),
            Instruction(1, CilOperation.NewObject, new CilOperand.MethodInstance(
                Instance(ordinary, declaringType))),
            Instruction(2, CilOperation.Box, new CilOperand.TypeIdentity(elementType)),
            Instruction(3, CilOperation.NewArray, new CilOperand.None()),
            Instruction(4, CilOperation.MaterializeType, new CilOperand.None()),
            Instruction(5, CilOperation.LoadFunction, new CilOperand.Entity(ordinary.Key)),
            Instruction(6, CilOperation.CallVirtual, new CilOperand.Entity(delegateInvoke.Key)),
            Instruction(7, CilOperation.CallVirtual, new CilOperand.Entity(virtualMethod.Key)),
            Instruction(8, CilOperation.LoadVirtualFunction,
                new CilOperand.Entity(virtualMethod.Key)),
            Instruction(9, CilOperation.LoadString, new CilOperand.UserString("literal")),
            Instruction(10, CilOperation.IsInstance, new CilOperand.Entity(declaringKey)),
            Instruction(11, CilOperation.Call, new CilOperand.MethodInstance(
                Instance(ordinary, declaringType))),
            Instruction(12, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
            Instruction(13, CilOperation.Box, new CilOperand.TypeIdentity(elementType)),
            Instruction(14, CilOperation.CallVirtual, new CilOperand.Entity(ordinary.Key)),
            Instruction(15, CilOperation.Constrained,
                new CilOperand.TypeIdentity(declaringType)),
            Instruction(16, CilOperation.CallVirtual, new CilOperand.Entity(virtualMethod.Key)),
        };
        var body = new CilMethodBody(method, 1, [], [.. instructions]);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new RecordingTypeFinder(),
            new FixedTypeIdentityResolver(),
            new OffsetCalledMethodResolver(),
            new FixedTypeOperandResolver(elementType),
            new DispatchSiteKeyBuilder(),
            new RecordingDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, declaringType), body));

        Assert.Contains(CliTypeIdentity.SzArray(elementType), result.ConstructedTypes);
        Assert.Contains(CliTypeIdentity.SzArray(elementType), result.AllocatedTypes);
        Assert.Contains("literal", result.Strings);
        Assert.Contains(field, result.Fields);
        Assert.Contains(Instance(ordinary, declaringType), result.CallableMethods);
        Assert.Equal(3, result.Dispatches.Length);
        Assert.Contains(
            result.Dispatches,
            dispatch => dispatch.Declaration.IlOffset == 16);
        Assert.Contains(result.Entities, entity => entity.Entity == declaringKey);
        Assert.Contains(result.Methods, reference => reference.Method.Definition == ordinary);
        Assert.Contains(result.Types, type => type == new EntityKey(assembly, 101));
    }

    [Theory]
    [InlineData(CilOperation.CallVirtual)]
    [InlineData(CilOperation.LoadVirtualFunction)]
    public void AnalyzerDispatchesVirtualCallsAtMethodEntry(CilOperation operation)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 400);
        var declaringType = CliTypeIdentity.Named(assembly, "Fixtures", "Target", false);
        var method = Method(401, "Run", declaringKey);
        var virtualMethod = Method(402, "Virtual", declaringKey) with
        {
            IsVirtual = true,
        };
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(Instance(virtualMethod, declaringType)),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, declaringType),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, operation,
                    new CilOperand.Entity(virtualMethod.Key))])));

        Assert.Single(result.Dispatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzerDiscoversBoxedValueTypeVirtualFunctionLoads(bool constructed)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 500);
        var definition = CliTypeIdentity.Named(assembly, "Fixtures", "Counter", true);
        var declaringType = constructed
            ? CliTypeIdentity.GenericInstantiation(definition,
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
            : definition;
        var caller = Instance(Method(501, "Run", declaringKey), declaringType);
        var target = Instance(Method(502, "Next", declaringKey) with
        {
            IsStatic = false,
            IsVirtual = true,
            IsFinal = true,
        }, declaringType);
        var instruction = Instruction(7, CilOperation.LoadVirtualFunction,
            new CilOperand.MethodInstance(target));
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(target),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = ((IReachabilityInstructionAnalyzer)analyzer).Analyze(
            new ReachabilityInstructionRequest(caller,
                new CilMethodBody(caller.Definition, 1, [], [instruction])));

        var dispatch = Assert.Single(result.Dispatches);
        Assert.Equal(new DispatchSiteKeyBuilder().Build(caller.CanonicalName, 7), dispatch.Key);
        Assert.Equal(target, dispatch.Declaration.Declaration);
        Assert.Equal(CilOperation.LoadVirtualFunction, dispatch.Declaration.Operation);
        Assert.Empty(result.Methods);
        Assert.Empty(result.CallableMethods);
        Assert.Single(result.CallSites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzerOmitsValueTypeVirtualDispatches(bool includeValueTypeConstraint)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(assembly, "Fixtures", "Target", false);
        var method = Method(1, "Run", declaringKey);
        var virtualKey = new EntityKey(assembly, 6);
        var valueType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var call = Instruction(
            14,
            CilOperation.CallVirtual,
            new CilOperand.Entity(virtualKey));
        var instructions = includeValueTypeConstraint
            ? ImmutableArray.Create(
                Instruction(
                    13,
                    CilOperation.Constrained,
                    new CilOperand.TypeIdentity(valueType)),
                call)
            : ImmutableArray.Create(call);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new RecordingTypeFinder(),
            new FixedTypeIdentityResolver(),
            new OffsetCalledMethodResolver(),
            new FixedTypeOperandResolver(valueType),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(method, declaringType),
            new CilMethodBody(method, 1, [], instructions)));

        Assert.Empty(result.Dispatches);
    }

    [Fact]
    public void AnalyzerRejectsConstraintWithoutTypeIdentityBeforeResolvingCalls()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 1);
        var declaringType = CliTypeIdentity.Named(assembly, "Fixtures", "Target", false);
        var method = Method(1, "Run", declaringKey);
        var calledMethods = new RecordingCalledMethodResolver();
#pragma warning disable CA1859 // Exercise the one-action capability contract directly.
        IReachabilityInstructionAnalyzer analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            calledMethods,
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));
#pragma warning restore CA1859
        var request = new ReachabilityInstructionRequest(
            Instance(method, declaringType),
            new CilMethodBody(
                method,
                1,
                [],
                [Instruction(0, CilOperation.Constrained, new CilOperand.None())]));

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze(request));
        Assert.Equal(0, calledMethods.CallCount);
    }

    [Fact]
    public void MemberTokensProduceDescriptorRootsWithoutExecutableOrStorageRoots()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 0x02000001);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var caller = Method(0x06000001, "Run", declaringKey);
        var target = Instance(Method(0x06000002, "Target", declaringKey), declaringType);
        var fieldDefinition = new FieldDefinitionModel(
            new EntityKey(assembly, 0x04000001),
            declaringKey,
            "Value",
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            isStatic: true);
        var field = new FieldInstanceModel(
            fieldDefinition,
            declaringType,
            fieldDefinition.SignatureType);
        var body = new CilMethodBody(
            caller,
            2,
            [],
            [
                Instruction(
                    0,
                    CilOperation.LoadMethodToken,
                    new CilOperand.MethodInstance(target)),
                Instruction(1, CilOperation.Pop, new CilOperand.None()),
                Instruction(
                    2,
                    CilOperation.LoadFieldToken,
                    new CilOperand.FieldInstance(field)),
                Instruction(3, CilOperation.Pop, new CilOperand.None()),
            ]);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, declaringType),
            body));

        Assert.Equal(target, Assert.Single(result.MethodDescriptors));
        Assert.Equal(field, Assert.Single(result.FieldDescriptors));
        Assert.Empty(result.Methods);
        Assert.Empty(result.Fields);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public void TypeFactsLookupRecordsOnlyTheBoundedFactsDemand()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 0x02000001);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var caller = Method(0x06000001, "Run", declaringKey);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, declaringType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.GetTypeFacts, new CilOperand.None())])));

        Assert.True(result.RequiresTypeFacts);
        Assert.Empty(result.RuntimeTypes);
        Assert.Empty(result.ConstructedTypes);
        Assert.Empty(result.AllocatedTypes);
        Assert.Empty(result.MethodDescriptors);
        Assert.Empty(result.FieldDescriptors);
    }

    [Fact]
    public void DelegateInvokeReaderRecordsIndependentDescriptorDemand()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callerTypeKey = new EntityKey(assembly, 0x02000001);
        var typeKey = new EntityKey(assembly, 0x02000002);
        var callerType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var runtimeType = CliTypeIdentity.Named(
            assembly,
            "System",
            "Type",
            isValueType: false);
        var methodInfo = CliTypeIdentity.Named(
            assembly,
            "System.Reflection",
            "MethodInfo",
            isValueType: false);
        var caller = Method(0x06000001, "Run", callerTypeKey);
        var readerDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000002),
            typeKey,
            "GetDelegateInvokeMethod",
            IsStatic: false,
            MethodSignatureModel.Create(methodInfo),
            1);
        var reader = new MethodInstanceModel(
            readerDefinition,
            runtimeType,
            [],
            readerDefinition.Signature);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(reader),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, callerType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.Call, new CilOperand.None())])));

        Assert.True(result.RequiresDelegateInvoke);
        Assert.False(result.RequiresTypeFacts);
        Assert.False(result.RequiresMemberNames);
        Assert.Equal(RuntimeTypeNamePayload.None, result.TypeNamePayload);
    }

    [Fact]
    public void GenericArgumentQueryRecordsIndependentMetadataDemand()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callerTypeKey = new EntityKey(assembly, 0x02000001);
        var typeKey = new EntityKey(assembly, 0x02000002);
        var callerType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var runtimeType = CliTypeIdentity.Named(
            assembly,
            "System",
            "Type",
            isValueType: false);
        var caller = Method(0x06000001, "Run", callerTypeKey);
        var queryDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000002),
            typeKey,
            "GetGenericArguments",
            IsStatic: false,
            MethodSignatureModel.Create(runtimeType),
            1);
        var query = new MethodInstanceModel(
            queryDefinition,
            runtimeType,
            [],
            queryDefinition.Signature);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(query),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, callerType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.Call, new CilOperand.None())])));

        Assert.True(result.RequiresGenericArguments);
        Assert.False(result.RequiresTypeFacts);
        Assert.False(result.RequiresDelegateInvoke);
    }

    [Fact]
    public void MemberInfoNameGetterRecordsIndependentMemberNameDemand()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callerTypeKey = new EntityKey(assembly, 0x02000001);
        var memberInfoKey = new EntityKey(assembly, 0x02000002);
        var callerType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var memberInfoType = CliTypeIdentity.Named(
            assembly,
            "System.Reflection",
            "MemberInfo",
            isValueType: false);
        var caller = Method(0x06000001, "Run", callerTypeKey);
        var getterDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000002),
            memberInfoKey,
            "get_Name",
            IsStatic: false,
            MethodSignatureModel.Create(CliTypeIdentity.Primitive(
                "string",
                CliValueKind.ManagedReference,
                isValueType: false)),
            1)
        {
            IsVirtual = true,
        };
        var getter = new MethodInstanceModel(
            getterDefinition,
            memberInfoType,
            [],
            getterDefinition.Signature);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(getter),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, callerType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.CallVirtual, new CilOperand.None())])));

        Assert.True(result.RequiresMemberNames);
        Assert.False(result.RequiresTypeFacts);
    }

    [Theory]
    [InlineData("GetRuntimeName")]
    [InlineData("GetRuntimeNamespace")]
    [InlineData("GetRuntimeFullName")]
    [InlineData("GetRuntimeDisplayName")]
    public void TypeNameOperationsRecordIndependentTypeNameDemand(string methodName)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callerTypeKey = new EntityKey(assembly, 0x02000001);
        var typeKey = new EntityKey(assembly, 0x02000002);
        var callerType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var runtimeType = CliTypeIdentity.Named(
            assembly,
            "System",
            "Type",
            isValueType: false);
        var caller = Method(0x06000001, "Run", callerTypeKey);
        var methodDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 0x06000002),
            typeKey,
            methodName,
            IsStatic: false,
            MethodSignatureModel.Create(CliTypeIdentity.Primitive(
                "string",
                CliValueKind.ManagedReference,
                isValueType: false)),
            1)
        {
            IsVirtual = false,
        };
        var method = new MethodInstanceModel(
            methodDefinition,
            runtimeType,
            [],
            methodDefinition.Signature);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new SingleCalledMethodResolver(method),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var result = analyzer.Analyze(new ReachabilityInstructionRequest(
            Instance(caller, callerType),
            new CilMethodBody(
                caller,
                1,
                [],
                [Instruction(0, CilOperation.Call, new CilOperand.None())])));

        var expected = methodName switch
        {
            "GetRuntimeName" => RuntimeTypeNamePayload.Name,
            "GetRuntimeNamespace" => RuntimeTypeNamePayload.Namespace,
            "GetRuntimeFullName" => RuntimeTypeNamePayload.FullName,
            "GetRuntimeDisplayName" => RuntimeTypeNamePayload.DisplayName,
            _ => throw new InvalidOperationException(methodName),
        };
        Assert.Equal(expected, result.TypeNamePayload);
        Assert.False(result.RequiresTypeFacts);
        Assert.False(result.RequiresMemberNames);
    }

    [Theory]
    [InlineData(CilOperation.LoadMethodToken)]
    [InlineData(CilOperation.LoadFieldToken)]
    public void MemberTokensRejectMissingResolvedOperands(CilOperation operation)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var declaringKey = new EntityKey(assembly, 0x02000001);
        var declaringType = CliTypeIdentity.Named(
            assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var caller = Method(0x06000001, "Run", declaringKey);
        var analyzer = new ReachabilityInstructionAnalyzer(
            new UnusedTypeFinder(),
            new UnusedTypeIdentityResolver(),
            new UnusedCalledMethodResolver(),
            new UnusedTypeOperandResolver(),
            new DispatchSiteKeyBuilder(),
            new UnusedDelegateMethodClassifier(),
            new NullableTypeResolver(),
            new ManagedCallSiteFactory(new ManagedMethodIdentityFactory()),
            new NativeCallbackDeclarationValidator(),
            new DispatchReceiverBoundResolver(new UnusedTypeOperandResolver()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            analyzer.Analyze(new ReachabilityInstructionRequest(
                Instance(caller, declaringType),
                new CilMethodBody(
                    caller,
                    1,
                    [],
                    [Instruction(0, operation, new CilOperand.None())]))));

        Assert.Contains("requires", exception.Message, StringComparison.Ordinal);
    }

    private static CilInstruction Instruction(
        int offset,
        CilOperation operation,
        CilOperand operand) =>
        new(offset, offset + 1, operation, operand);

    private static MethodDefinitionModel Method(
        int token,
        string name,
        EntityKey declaringType) =>
        new(
            new EntityKey(new AssemblyIdentity("in-memory"), token),
            declaringType,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);

    private static MethodInstanceModel Instance(
        MethodDefinitionModel method,
        CliTypeIdentity declaringType) =>
        new(method, declaringType, [], method.Signature);

    private sealed class RecordingTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => new(
            new EntityKey(new AssemblyIdentity("in-memory"), 300),
            "System",
            "Type",
            false,
            [],
            []);
    }

    private sealed class FixedTypeIdentityResolver(
        CliTypeIdentity? resolvedType = default) : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            resolvedType ?? CliTypeIdentity.Named(
                new AssemblyIdentity("in-memory"),
                "System",
                "Type",
                false);
    }

    private sealed class FixedTypeFinder(TypeDefinitionModel type) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName)
        {
            Assert.Equal(type.FullName, fullName);
            return type;
        }
    }

    private sealed class FixedTypeOperandResolver(CliTypeIdentity elementType) : ITypeOperandResolver
    {
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) => elementType;
    }

    private sealed class OffsetCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => instruction.Offset switch
        {
            0 => Instance(Method(2, ".ctor", new EntityKey(
                new AssemblyIdentity("in-memory"), 101)),
                CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Target", false)),
            1 => Instance(Method(4, "Call", new EntityKey(
                new AssemblyIdentity("in-memory"), 100)),
                CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Target", false)),
            5 => Instance(Method(4, "Call", new EntityKey(
                new AssemblyIdentity("in-memory"), 100)),
                CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Target", false)),
            6 => Instance(Method(3, "Invoke", new EntityKey(
                new AssemblyIdentity("in-memory"), 101)),
                CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Target", false)),
            7 or 8 or 16 => Instance(Method(5, "Virtual", new EntityKey(
                new AssemblyIdentity("in-memory"), 100)) with
            { IsVirtual = true },
                CliTypeIdentity.Named(new AssemblyIdentity("in-memory"), "Fixtures", "Target", false)),
            14 => Instance(Method(6, "ValueVirtual", new EntityKey(
                new AssemblyIdentity("in-memory"), 100)) with
            { IsVirtual = true },
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
            _ => null,
        };
    }

    private sealed class RecordingDelegateMethodClassifier : IDelegateMethodClassifier
    {
        public DelegateMethodKind Classify(MethodInstanceModel method) => method.Definition.Name switch
        {
            ".ctor" => DelegateMethodKind.Constructor,
            "Invoke" => DelegateMethodKind.Invoke,
            _ => DelegateMethodKind.None,
        };
    }

    private sealed class UnusedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => throw new InvalidOperationException();
    }

    private sealed class UnusedTypeIdentityResolver : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            throw new InvalidOperationException();
    }

    private sealed class RecordingCalledMethodResolver : ICalledMethodResolver
    {
        public int CallCount { get; private set; }

        public MethodInstanceModel? Resolve(CilInstruction instruction)
        {
            CallCount++;
            return null;
        }
    }

    private sealed class UnusedCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => null;
    }

    private sealed class SingleCalledMethodResolver(MethodInstanceModel method) :
        ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class UnusedTypeOperandResolver : ITypeOperandResolver
    {
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedDelegateMethodClassifier : IDelegateMethodClassifier
    {
        public DelegateMethodKind Classify(MethodInstanceModel method) =>
            DelegateMethodKind.None;
    }
}
