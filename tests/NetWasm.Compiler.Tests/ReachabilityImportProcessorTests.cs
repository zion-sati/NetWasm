using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ReachabilityImportClassifierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierValidatesNativeDeclarationsBeforeAnyOtherClassification(bool reject)
    {
        var method = Method(40, MethodSignatureModel.Create(CliValueKind.I4)) with
        {
            NativeImport = new("mule", "increment", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        var planner = new RecordingNativeDeclarationValidator(reject);
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(
            new ReachabilityImportClassifier(
                new UnusedTypeFinder(), new UnusedTypeDefinitionResolver(),
                new UnusedMethodRepository(), new UnusedDelegateTypeRecognizer(),
                new NoAsyncBindingResolver(), new UnusedIntrinsicRegistry(),
                new SimpleSymbolFormatter(), new EnumMetadataRequirementClassifier(), planner,
                new UnusedConversionResolver()));
        var request = Request(method);

        if (reject)
        {
            var error = Assert.Throws<CompilerException>(() => classifier.Classify(request));
            Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        }
        else
        {
            var result = classifier.Classify(request);
            Assert.True(result.IsHandled);
            Assert.Empty(result.Types);
            Assert.Empty(result.ConstructedTypes);
            Assert.Empty(result.AllocatedTypes);
            Assert.Empty(result.Fields);
            Assert.Empty(result.EnqueuedMethods);
            Assert.Empty(result.Exceptions);
            Assert.Empty(result.HostCallbacks);
            Assert.Null(result.JavaScriptImport);
            Assert.Null(result.WitImport);
            Assert.Null(result.JavaScriptAsyncBinding);
        }
        Assert.Same(request.Method, planner.LastMethod);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public void NativeDeclarationValidatorIsRequiredByClassifierAndFactory()
    {
        var classifierError = Assert.Throws<ArgumentNullException>(() =>
            new ReachabilityImportClassifier(
                new UnusedTypeFinder(), new UnusedTypeDefinitionResolver(),
                new UnusedMethodRepository(), new UnusedDelegateTypeRecognizer(),
                new NoAsyncBindingResolver(), new UnusedIntrinsicRegistry(),
                new SimpleSymbolFormatter(), new EnumMetadataRequirementClassifier(), null!,
                new UnusedConversionResolver()));
        Assert.Equal("nativeDeclarations", classifierError.ParamName);
        var factoryError = Assert.Throws<ArgumentNullException>(() =>
            new ReachabilityImportClassifierFactory(new EnumMetadataRequirementClassifier(), null!));
        Assert.Equal("nativeDeclarations", factoryError.ParamName);
    }

    [Fact]
    public void FactoryPassesTheInjectedDeclarationContractToItsClassifier()
    {
        var method = Method(41, MethodSignatureModel.Create(CliValueKind.Void)) with
        {
            NativeImport = new("mule", "reset", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        var planner = new RecordingNativeDeclarationValidator(false);
        var factory = Assert.IsAssignableFrom<IReachabilityImportClassifierFactory>(
            new ReachabilityImportClassifierFactory(new EnumMetadataRequirementClassifier(), planner));
        var classifier = factory.Create(
            new UnusedTypeFinder(), new UnusedTypeDefinitionResolver(),
            new UnusedMethodRepository(), new UnusedDelegateTypeRecognizer(),
            new NoAsyncBindingResolver(), new UnusedConversionResolver(), new UnusedIntrinsicRegistry(),
            new SimpleSymbolFormatter());

        Assert.True(classifier.Classify(Request(method)).IsHandled);
        Assert.Equal(1, planner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSignatureTypesEnterLayoutClosureOnlyAfterDeclarationValidation(bool reject)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var aggregate = CliTypeIdentity.Named(assembly, "Fixture", "Pair", isValueType: true);
        var generic = CliTypeIdentity.Named(assembly, "Fixture", "Box`1", isValueType: true);
        var closed = CliTypeIdentity.GenericInstantiation(generic,
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var opaque = CliTypeIdentity.UnmanagedPointer(aggregate);
        var reference = CliTypeIdentity.Named(assembly, "Fixture", "Reference", isValueType: false);
        var declarations = new RecordingNativeDeclarationValidator(reject);
        var resolver = new RecordingLayoutTypeResolver(declarations, aggregate, closed);
        var signature = MethodSignatureModel.Create(aggregate, aggregate, closed, opaque, reference,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var method = Method(42, signature) with
        {
            NativeImport = new("mule", "aggregate", System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(
            new ReachabilityImportClassifier(
                new UnusedTypeFinder(), resolver, new UnusedMethodRepository(),
                new UnusedDelegateTypeRecognizer(), new NoAsyncBindingResolver(),
                new UnusedIntrinsicRegistry(), new SimpleSymbolFormatter(),
                new EnumMetadataRequirementClassifier(), declarations,
                new UnusedConversionResolver()));

        if (reject)
        {
            var error = Assert.Throws<CompilerException>(() => classifier.Classify(Request(method)));
            Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
            Assert.Empty(resolver.Calls);
        }
        else
        {
            var analysis = classifier.Classify(Request(method));
            Assert.True(analysis.IsHandled);
            Assert.Equal([aggregate, closed], resolver.Calls.ToArray());
            Assert.Equal([new EntityKey(assembly, 501), new EntityKey(assembly, 502)],
                analysis.Types.ToArray());
            Assert.Equal(closed, Assert.Single(analysis.ConstructedTypes));
            Assert.Empty(analysis.AllocatedTypes);
            Assert.Empty(analysis.Fields);
            Assert.Empty(analysis.EnqueuedMethods);
            Assert.Empty(analysis.Exceptions);
            Assert.Empty(analysis.HostCallbacks);
        }
        Assert.Equal(1, declarations.Calls);
    }

    [Fact]
    public void ClassifierRecognizesIntrinsicMethodsInItsResult()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            new EntityKey(assembly, 1),
            "get_Length",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);
        var classifier = new ReachabilityImportClassifier(
            new UnusedTypeFinder(),
            new UnusedTypeDefinitionResolver(),
            new UnusedMethodRepository(),
            new UnusedDelegateTypeRecognizer(),
            new NoAsyncBindingResolver(),
            new SingleIntrinsicRegistry(method.Key),
            new SimpleSymbolFormatter(),
            new EnumMetadataRequirementClassifier(),
            new UnusedNativeDeclarationValidator(),
            new UnusedConversionResolver());
        var request = new ReachabilityImportRequest(
            new MethodInstanceModel(
                method,
                CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false),
                [],
                method.Signature));

        Assert.True(classifier.Classify(request).IsHandled);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.ValueTypeEquals, "Equals")]
    [InlineData(RuntimeIntrinsic.ValueTypeGetHashCode, "GetValueHashCode")]
    public void ClassifierRetainsManagedHelperForValueTypeIntrinsic(
        RuntimeIntrinsic intrinsic,
        string helperName)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var objectKey = new EntityKey(assembly, 30);
        var intrinsicMethod = Method(31, MethodSignatureModel.Create(CliValueKind.I4));
        var equals = new MethodDefinitionModel(
            new EntityKey(assembly, 32),
            objectKey,
            "Equals",
            true,
            MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference),
            1);
        var hashCode = new MethodDefinitionModel(
            new EntityKey(assembly, 33),
            objectKey,
            "GetValueHashCode",
            true,
            MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference),
            1);
        var objectType = new TypeDefinitionModel(
            objectKey,
            "System",
            "Object",
            false,
            [],
            [equals.Key, hashCode.Key]);
        var classifier = CreateClassifier(
            new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(equals, hashCode),
            new NamedTypeFinder(objectType),
            new FixedDelegateRecognizer(false),
            new SingleIntrinsicRegistry(intrinsicMethod.Key, intrinsic));

        var result = classifier.Classify(Request(intrinsicMethod));

        var helper = Assert.Single(result.EnqueuedMethods);
        Assert.Equal(helperName, helper.Definition.Name);
    }

    [Fact]
    public void ClassifierRecognizesJavaScriptAndWitImportContracts()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var byteArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var hostObject = CliTypeIdentity.Named(
            assembly,
            "System.Runtime.InteropServices.JavaScript",
            "JSObject",
            false);
        var javaScriptMethod = Method(
            10,
            MethodSignatureModel.Create(byteArray, hostObject),
            new InteropImportDeclaration("read", "env"));
        var hostObjectMethod = Method(
            11,
            MethodSignatureModel.Create(hostObject),
            new InteropImportDeclaration("get", "env"));
        var witMethod = Method(
            12,
            MethodSignatureModel.Create(CliValueKind.Void),
            null,
            new WitImportDeclaration("world", "read"));
        var classifier = CreateClassifier(
            new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(),
            new FixedTypeFinder(),
            new FixedDelegateRecognizer(false),
            new FalseIntrinsicRegistry());

        var javaScript = classifier.Classify(Request(javaScriptMethod));
        Assert.True(javaScript.IsHandled);
        Assert.Same(javaScriptMethod, javaScript.JavaScriptImport);
        Assert.Contains(byteArray, javaScript.AllocatedTypes);
        Assert.Contains(
            javaScript.Exceptions,
            requirement => requirement.Kind == ManagedExceptionKind.JSException);

        var hostResult = classifier.Classify(Request(hostObjectMethod));
        Assert.True(hostResult.IsHandled);
        Assert.Contains(
            hostResult.Exceptions,
            requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);

        var numbers = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("f8", CliValueKind.F8));
        var numbersResult = classifier.Classify(Request(Method(
            13,
            MethodSignatureModel.Create(numbers),
            new InteropImportDeclaration("numbers", "env"))));
        Assert.True(numbersResult.IsHandled);
        Assert.Contains(numbers, numbersResult.AllocatedTypes);
        Assert.Contains(numbers, numbersResult.ConstructedTypes);

        var flags = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("bool", CliValueKind.I4));
        var flagsResult = classifier.Classify(Request(Method(
            14,
            MethodSignatureModel.Create(flags),
            new InteropImportDeclaration("flags", "env"))));
        Assert.True(flagsResult.IsHandled);
        Assert.DoesNotContain(flags, flagsResult.AllocatedTypes);

        var wit = classifier.Classify(Request(witMethod));
        Assert.True(wit.IsHandled);
        Assert.Same(witMethod, wit.WitImport);
    }

    [Theory]
    [InlineData("JSObject", true)]
    [InlineData("JSObject", false)]
    [InlineData("JSSubscription", true)]
    [InlineData("JSSubscription", false)]
    public void HostResultsAreRuntimeAllocationsButParametersAreNot(string name, bool returnsObject)
    {
        var hostObject = CliTypeIdentity.Named(new AssemblyIdentity("in-memory"),
            "System.Runtime.InteropServices.JavaScript", name, false);
        var signature = returnsObject
            ? MethodSignatureModel.Create(hostObject)
            : MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), hostObject);
        var method = Method(14, signature, new InteropImportDeclaration("handle", "consumer.handles"));
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(CreateClassifier(
            new ConfigurableTypeDefinitionResolver(), new ConfigurableMethodRepository(),
            new FixedTypeFinder(), new FixedDelegateRecognizer(false), new FalseIntrinsicRegistry()));

        var result = classifier.Classify(Request(method));

        Assert.True(result.IsHandled);
        Assert.Same(method, result.JavaScriptImport);
        Assert.Empty(result.ConstructedTypes);
        if (returnsObject)
        {
            Assert.Equal(hostObject, Assert.Single(result.AllocatedTypes));
            Assert.Equal(new EntityKey(new AssemblyIdentity("in-memory"), 500), Assert.Single(result.Types));
            Assert.Contains(result.Exceptions, e => e.Kind == ManagedExceptionKind.OutOfMemory);
        }
        else
        {
            Assert.Empty(result.AllocatedTypes);
            Assert.Empty(result.Types);
            Assert.DoesNotContain(result.Exceptions, e => e.Kind == ManagedExceptionKind.OutOfMemory);
        }
    }

    [Fact]
    public void ClassifierRecognizesDelegateCallbacksAndOrdinaryBodies()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callbackType = CliTypeIdentity.Named(assembly, "Fixtures", "Callback", false);
        var stringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var byteArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var callbackInvoke = Method(
            20,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                stringType,
                byteArray),
            name: "Invoke");
        var callback = new TypeDefinitionModel(
            new EntityKey(assembly, 21),
            "Fixtures",
            "Callback",
            false,
            [],
            [callbackInvoke.Key]);
        var callbackMethod = Method(
            22,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                callbackType),
            new InteropImportDeclaration("invoke", "env"));
        var classifier = CreateClassifier(
            new ConfigurableTypeDefinitionResolver(callback, callbackInvoke),
            new ConfigurableMethodRepository(callbackInvoke),
            new FixedTypeFinder(),
            new FixedDelegateRecognizer(true),
            new FalseIntrinsicRegistry());

        var result = classifier.Classify(Request(callbackMethod));

        var hostCallback = Assert.Single(result.HostCallbacks);
        Assert.Equal(0, hostCallback.ParameterIndex);
        Assert.Contains(byteArray, result.AllocatedTypes);
        Assert.Contains(
            result.Exceptions,
            requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);

        var bodyMethod = Method(
            23,
            MethodSignatureModel.Create(CliValueKind.Void),
            null,
            null,
            relativeVirtualAddress: 1);
        Assert.False(classifier.Classify(Request(bodyMethod)).IsHandled);
    }

    [Fact]
    public void ClassifierRejectsBodylessMethodsWithoutImportDeclarations()
    {
        var method = Method(24, MethodSignatureModel.Create(CliValueKind.Void));
        var classifier = CreateClassifier(
            new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(),
            new FixedTypeFinder(),
            new FixedDelegateRecognizer(false),
            new FalseIntrinsicRegistry());

        var error = Assert.Throws<CompilerException>(() => classifier.Classify(Request(method)));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodylessAccessorsUseManagedAnalysisIncludingManagedBindingFailures(bool malformed)
    {
        var method = Method(24, MethodSignatureModel.Create(CliValueKind.Void)) with
        {
            UnsafeAccessor = new(1, "Target", true, malformed, false),
        };
        var classifier = CreateClassifier(new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(), new FixedTypeFinder(),
            new FixedDelegateRecognizer(false), new FalseIntrinsicRegistry());

        var result = classifier.Classify(Request(method));

        Assert.False(method.HasBody);
        Assert.True(method.HasManagedBody);
        Assert.False(result.IsHandled);
        Assert.Null(result.JavaScriptImport);
        Assert.Null(result.WitImport);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ClassifierRetainsAsyncProtocolForOrdinaryOutwardBoundary(bool process, bool missingObserver)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var taskType = CliTypeIdentity.Named(
            assembly,
            "System.Threading.Tasks",
            "Task",
            isValueType: false);
        var method = Method(
            25,
            MethodSignatureModel.Create(taskType),
            relativeVirtualAddress: 1);
        var instance = new MethodInstanceModel(
            method,
            taskType,
            [],
            method.Signature);
        var status = new FieldInstanceModel(
            new FieldDefinitionModel(
                new EntityKey(assembly, 26),
                method.DeclaringType,
                "Status",
                CliValueKind.I4,
                false),
            taskType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var binding = new JavaScriptAsyncMethodBinding(
            method.Key,
            new(JavaScriptAsyncReturnKind.Task, null),
            taskType,
            instance,
            instance,
            instance)
        {
            StatusField = status,
            ValueTaskTaskField = status,
            ValueTaskAsTask = instance,
            GetVoidResult = missingObserver ? null : instance with { Definition = method with { Name = "GetVoidResult" } },
        };
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(
            new ReachabilityImportClassifier(
                new FixedTypeFinder(),
                new ConfigurableTypeDefinitionResolver(),
                new ConfigurableMethodRepository(),
                new FixedDelegateRecognizer(false),
                new FixedAsyncBindingResolver(binding),
                new FalseIntrinsicRegistry(),
                new SimpleSymbolFormatter(),
                new EnumMetadataRequirementClassifier(),
                new UnusedNativeDeclarationValidator(),
                new UnusedConversionResolver()));

        var request = new ReachabilityImportRequest(
            instance,
            IsOutwardBoundary: true,
            IsProcessEntryPoint: process);
        if (missingObserver)
        {
            Assert.Throws<InvalidOperationException>(() => classifier.Classify(request));
            return;
        }
        var result = classifier.Classify(request);

        Assert.Same(binding, result.JavaScriptAsyncBinding);
        Assert.Contains(status, result.Fields);
        Assert.Contains(instance, result.EnqueuedMethods);
        Assert.Contains(binding.GetVoidResult!, result.EnqueuedMethods);
        Assert.Contains(
            result.Exceptions,
            requirement => requirement.Kind == ManagedExceptionKind.NullReference);
        Assert.Contains(
            result.Exceptions,
            requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierRetainsAsyncImportSettersResultFieldsAndClosedDeclaringTypes(bool outward)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var taskType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "System.Threading.Tasks", "Task`1", false),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var taskBaseType = CliTypeIdentity.Named(assembly, "System.Threading.Tasks", "Task", false);
        var owner = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Fixtures", "Service`1", false),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        var method = Method(60, MethodSignatureModel.Create(taskType),
            new("get", "host") { IsPromise = true });
        var instance = new MethodInstanceModel(method, owner, [], method.Signature);
        var setResult = Setter(65, "SetResult", taskType, CliValueKind.I4);
        var setException = Setter(66, "SetException", taskBaseType, CliValueKind.ManagedReference);
        var setCanceled = Setter(67, "SetCanceledForJavaScriptInterop", taskBaseType);
        var getVoidResult = Setter(68, "GetVoidResult", taskBaseType);
        var status = new FieldInstanceModel(
            new(new(assembly, 61), new(assembly, 81), "Status", CliValueKind.I4, false),
            taskBaseType, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var resultField = new FieldInstanceModel(
            new(new(assembly, 62), new(assembly, 80), "Result", CliValueKind.I4, false),
            taskType, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var binding = new JavaScriptAsyncMethodBinding(method.Key,
            new(JavaScriptAsyncReturnKind.Task, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            taskType, setResult, setException, setCanceled)
        {
            StatusField = status,
            ResultField = resultField,
            GetVoidResult = getVoidResult,
        };
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(new ReachabilityImportClassifier(
            new FixedTypeFinder(), new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(), new FixedDelegateRecognizer(false),
            new FixedAsyncBindingResolver(binding), new FalseIntrinsicRegistry(),
            new SimpleSymbolFormatter(), new EnumMetadataRequirementClassifier(), new UnusedNativeDeclarationValidator(),
            new UnusedConversionResolver()));

        var analysis = classifier.Classify(new(instance, outward));

        Assert.Same(binding, analysis.JavaScriptAsyncBinding);
        Assert.Equal([status, resultField], analysis.Fields.ToArray());
        Assert.Equal(outward ? [getVoidResult, setResult, setException, setCanceled] : [setResult, setException, setCanceled],
            analysis.EnqueuedMethods.ToArray());
        Assert.Contains(taskType, analysis.ConstructedTypes);
        Assert.Contains(owner, analysis.ConstructedTypes);
        Assert.Contains(taskType, analysis.AllocatedTypes);
        Assert.Equal(outward, analysis.Exceptions.Any(requirement => requirement.Kind == ManagedExceptionKind.NullReference));
        Assert.Contains(analysis.Exceptions, requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);

        MethodInstanceModel Setter(int token, string name, CliTypeIdentity declaringType, params CliValueKind[] parameters)
        {
            var signature = MethodSignatureModel.Create(CliValueKind.Void, parameters);
            var definition = Method(token, signature, name: name) with
            {
                IsStatic = false,
                DeclaringType = new(assembly, declaringType == taskType ? 80 : 81),
            };
            return new(definition, declaringType, [], signature);
        }
    }

    [Fact]
    public void ClassifierRootsStringImportAllocationAndEnumBoxingExceptions()
    {
        var imports = CreateClassifier(new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(), new FixedTypeFinder(),
            new FixedDelegateRecognizer(false), new FalseIntrinsicRegistry());
        var stringImport = Method(63,
            MethodSignatureModel.Create(CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)),
            new("text", "host"));

        var stringResult = imports.Classify(Request(stringImport));

        Assert.Same(stringImport, stringResult.JavaScriptImport);
        Assert.Contains(stringResult.Exceptions, requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);
        var boxing = Method(64, MethodSignatureModel.Create(CliValueKind.ManagedReference));
        var enums = CreateClassifier(new ConfigurableTypeDefinitionResolver(),
            new ConfigurableMethodRepository(), new FixedTypeFinder(),
            new FixedDelegateRecognizer(false), new SingleIntrinsicRegistry(boxing.Key, RuntimeIntrinsic.EnumToObject));

        var boxingResult = enums.Classify(Request(boxing));

        Assert.Contains(new(ManagedExceptionKind.Argument, "System.ArgumentException"), boxingResult.Exceptions);
        Assert.Contains(new(ManagedExceptionKind.ArgumentNull, "System.ArgumentNullException"), boxingResult.Exceptions);
        Assert.Empty(boxingResult.EnqueuedMethods);
    }

    [Fact]
    public void ClassifierSubstitutesClosedGenericCallbackParameters()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var callbackDefinition = new TypeDefinitionModel(new(assembly, 70), "Fixtures", "Callback`1", false, [], [new(assembly, 71)]);
        var callbackType = CliTypeIdentity.Named(assembly, "Fixtures", "Callback`1", false);
        var argument = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var closed = CliTypeIdentity.GenericInstantiation(callbackType, [argument]);
        var invoke = Method(71, MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.Void), CliTypeIdentity.GenericParameter(false, 0)),
            name: "Invoke") with
        { DeclaringType = callbackDefinition.Key, IsStatic = false };
        var import = Method(72, MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), closed),
            new("callback", "host"));
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(CreateClassifier(
            new ConfigurableTypeDefinitionResolver(callbackDefinition, invoke),
            new ConfigurableMethodRepository(invoke), new FixedTypeFinder(),
            new FixedDelegateRecognizer(true), new FalseIntrinsicRegistry()));

        var result = classifier.Classify(Request(import));

        var callback = Assert.Single(result.HostCallbacks);
        Assert.Equal(closed, callback.Invoke.DeclaringType);
        Assert.Equal(argument, Assert.Single(callback.Invoke.Signature.ParameterSignatureTypes));
        Assert.Equal(0, callback.ParameterIndex);
        Assert.Contains(callbackDefinition.Key, result.Types);
        Assert.Contains(result.Exceptions, requirement => requirement.Kind == ManagedExceptionKind.OutOfMemory);
    }

    private static ReachabilityImportClassifier CreateClassifier(
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        ITypeFinder typeFinder,
        IDelegateTypeRecognizer delegateTypes,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new(
            typeFinder,
            typeDefinitions,
            methods,
            delegateTypes,
            new NoAsyncBindingResolver(),
            intrinsics,
            new SimpleSymbolFormatter(),
            new EnumMetadataRequirementClassifier(),
            new UnusedNativeDeclarationValidator(),
            new UnusedConversionResolver());

    private static ReachabilityImportRequest Request(MethodDefinitionModel method) =>
        new(new MethodInstanceModel(
            method,
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false),
            [],
            method.Signature));

    private static MethodDefinitionModel Method(
        int token,
        MethodSignatureModel signature,
        InteropImportDeclaration? javaScriptImport = null,
        WitImportDeclaration? witImport = null,
        int relativeVirtualAddress = 0,
        string name = "Run") =>
        new(
            new EntityKey(new AssemblyIdentity("in-memory"), token),
            new EntityKey(new AssemblyIdentity("in-memory"), 1),
            name,
            true,
            signature,
            relativeVirtualAddress)
        {
            JSImport = javaScriptImport,
            WitImport = witImport,
        };

    private sealed class SingleIntrinsicRegistry(
        EntityKey method,
        RuntimeIntrinsic value = RuntimeIntrinsic.StringLength) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey candidate, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = value;
            return candidate == method;
        }
    }

    private sealed class FalseIntrinsicRegistry : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = default;
            return false;
        }
    }

    private sealed class UnusedNativeDeclarationValidator : INativeDeclarationValidator
    {
        public void Validate(MethodInstanceModel method) =>
            throw new InvalidOperationException();
    }

    private sealed class RecordingNativeDeclarationValidator(bool reject) : INativeDeclarationValidator
    {
        public int Calls { get; private set; }
        public MethodInstanceModel? LastMethod { get; private set; }

        public void Validate(MethodInstanceModel method)
        {
            Calls++;
            LastMethod = method;
            if (reject)
                throw new CompilerException(new(DiagnosticCode.NativeInterop, "Unsupported native declaration."));
        }
    }

    private sealed class RecordingLayoutTypeResolver(
        RecordingNativeDeclarationValidator declarations,
        params CliTypeIdentity[] types) : ITypeDefinitionResolver
    {
        public List<CliTypeIdentity> Calls { get; } = [];

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Assert.Equal(1, declarations.Calls);
            var index = Array.IndexOf(types, identity);
            Assert.True(index >= 0);
            Calls.Add(identity);
            var definition = identity.Shape == CliTypeShape.GenericInstantiation
                ? identity.ElementType! : identity;
            return new(new(definition.Assembly!.Value, 501 + index), "Fixture", definition.FullName!,
                true, [], []);
        }
    }

    private sealed class UnusedIntrinsicRegistry : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic) =>
            throw new InvalidOperationException();
    }

    private sealed class NoAsyncBindingResolver : IJavaScriptAsyncBindingResolver
    {
        public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method) => null;
    }

    private sealed class UnusedConversionResolver : IJavaScriptConversionResolver
    {
        public MethodInstanceModel Resolve(string name) =>
            throw new InvalidOperationException($"no boundary converts with {name}");
    }

    private sealed class FixedAsyncBindingResolver(JavaScriptAsyncMethodBinding binding) :
        IJavaScriptAsyncBindingResolver
    {
        public JavaScriptAsyncMethodBinding? Resolve(MethodDefinitionModel method) =>
            method.Key == binding.Method ? binding : null;
    }

    private sealed class UnusedDelegateTypeRecognizer : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => false;
    }

    private sealed class FixedDelegateRecognizer(bool result) : IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => result;
    }

    private sealed class UnusedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => throw new InvalidOperationException();
    }

    private sealed class FixedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => new(
            new EntityKey(new AssemblyIdentity("in-memory"), fullName.GetHashCode()),
            "System",
            fullName[(fullName.LastIndexOf('.') + 1)..],
            false,
            [],
            []);
    }

    private sealed class NamedTypeFinder(TypeDefinitionModel type) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            fullName == type.FullName
                ? type
                : throw new InvalidOperationException();
    }

    private sealed class UnusedTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new InvalidOperationException();
    }

    private sealed class ConfigurableTypeDefinitionResolver(
        TypeDefinitionModel? callback = null,
        MethodDefinitionModel? invoke = null) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            callback is not null && (identity.Shape == CliTypeShape.GenericInstantiation
                ? identity.ElementType!.FullName : identity.FullName) == callback.FullName
                ? callback
                : new(
                    new EntityKey(new AssemblyIdentity("in-memory"), 500),
                    identity.FullName is { } fullName && fullName.Contains('.')
                        ? fullName[..fullName.LastIndexOf('.')]
                        : "",
                    identity.FullName is { } name
                        ? name[(name.LastIndexOf('.') + 1)..]
                        : "Unknown",
                    identity.IsValueType,
                    [],
                    invoke is null ? [] : [invoke.Key]);
    }

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new InvalidOperationException();
    }

    private sealed class ConfigurableMethodRepository(
        params MethodDefinitionModel[] methods) : IMethodRepository
    {
        private readonly ImmutableDictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToImmutableDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class SimpleSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
