using System.Reflection;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeAbiPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("NativeFixture");
    private static readonly CliTypeIdentity DeclaringType = CliTypeIdentity.Named(
        Assembly, "Fixture", "NativeCalls", isValueType: false);

    [Theory]
    [InlineData("i4", CliValueKind.I4)]
    [InlineData("u4", CliValueKind.I4)]
    [InlineData("i8", CliValueKind.I8)]
    [InlineData("u8", CliValueKind.I8)]
    [InlineData("f4", CliValueKind.F4)]
    [InlineData("f8", CliValueKind.F8)]
    [InlineData("nativeint", CliValueKind.NativeInt)]
    [InlineData("nativeuint", CliValueKind.NativeInt)]
    public void PreservesScalarIdentityAndLowersScalarByrefs(string name, CliValueKind kind)
    {
        var type = CliTypeIdentity.Primitive(name, kind);
        var method = Method(type, type, CliTypeIdentity.ManagedByReference(type));

        var plan = ((INativeAbiPlanner)new NativeAbiPlanner(new NativeDeclarationValidator(), NativeAbiTestSupport.ScalarSignatures())).Plan(method);

        Assert.Equal(method.Definition.NativeImport, plan.Import);
        Assert.Equal(type, plan.Signature.ReturnSignatureType);
        Assert.Equal(type, plan.Signature.ParameterSignatureTypes[0]);
        Assert.Equal(CliValueKind.NativeInt, plan.Signature.ParameterTypes[1]);
    }

    [Fact]
    public void AcceptsVoidReturnAndOpaquePointersButRejectsVoidParametersAndByrefReturns()
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.Void);
        var pointer = CliTypeIdentity.UnmanagedPointer(type);
        var planner = new NativeAbiPlanner(new NativeDeclarationValidator(), NativeAbiTestSupport.ScalarSignatures());

        var plan = ((INativeAbiPlanner)planner).Plan(Method(type, pointer));

        Assert.Equal(CliValueKind.Void, plan.Signature.ReturnType);
        Assert.Equal(CliValueKind.NativeInt, Assert.Single(plan.Signature.ParameterTypes));
        Assert.Equal(CliValueKind.NativeInt, ((INativeAbiPlanner)planner).Plan(Method(pointer)).Signature.ReturnType);
        Reject(Method(type, type));
        Reject(Method(CliTypeIdentity.ManagedByReference(type)));
        Reject(Method(type, CliTypeIdentity.ManagedByReference(pointer)));
    }

    [Theory]
    [InlineData("bool", CliValueKind.I4)]
    [InlineData("char", CliValueKind.I4)]
    [InlineData("i1", CliValueKind.I4)]
    [InlineData("u1", CliValueKind.I4)]
    [InlineData("i2", CliValueKind.I4)]
    [InlineData("u2", CliValueKind.I4)]
    [InlineData("object", CliValueKind.ManagedReference)]
    [InlineData("string", CliValueKind.ManagedReference)]
    public void RejectsUnqualifiedPrimitiveAndMarshallingShapes(string name, CliValueKind kind)
    {
        var type = CliTypeIdentity.Primitive(name, kind);
        Reject(Method(type));
        Reject(Method(CliTypeIdentity.FromStackKind(CliValueKind.Void), type));
        Reject(Method(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.ManagedByReference(type)));
    }

    [Fact]
    public void RejectsUnqualifiedAggregatesReferencesArraysAndOpenPointers()
    {
        var aggregate = CliTypeIdentity.Named(Assembly, "Fixture", "Pair", isValueType: true);
        var reference = CliTypeIdentity.Named(Assembly, "Fixture", "Node", isValueType: false);
        foreach (var type in new[]
        {
            aggregate,
            reference,
            CliTypeIdentity.SzArray(aggregate),
            CliTypeIdentity.Array(aggregate, 2),
            CliTypeIdentity.ManagedByReference(aggregate),
            CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.GenericParameter(false, 0)),
        })
        {
            Reject(Method(type));
            Reject(Method(CliTypeIdentity.FromStackKind(CliValueKind.Void), type));
        }
    }

    [Fact]
    public void AcceptsClosedCdeclFunctionPointerParametersButRejectsTheirReturnShape()
    {
        var functionPointer = CliTypeIdentity.FunctionPointer(new(
            1,
            0,
            0,
            MethodSignatureModel.Create(CliValueKind.Void)));
        var planner = Assert.IsAssignableFrom<INativeAbiPlanner>(new NativeAbiPlanner(
            new NativeDeclarationValidator(),
            NativeAbiTestSupport.ScalarSignatures()));

        var plan = planner.Plan(Method(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            functionPointer));

        Assert.Equal(CliValueKind.NativeInt, Assert.Single(plan.Signature.ParameterTypes));
        Reject(Method(functionPointer));
    }

    private static MethodInstanceModel Method(CliTypeIdentity result, params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        return new(new(new(Assembly, 0x06000001), new(Assembly, 0x02000001),
            "Call", true, signature, 0)
        {
            NativeImport = new("fixture", "entry", MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        }, DeclaringType, [], signature);
    }

    [Fact]
    public void RequiresTheSharedSignaturePlanner()
    {
        var error = Assert.Throws<ArgumentNullException>(() => new NativeAbiPlanner(new NativeDeclarationValidator(), null!));
        Assert.Equal("signatures", error.ParamName);
    }

    [Fact]
    public void RequiresDeclarationValidationAndRejectsNullMethodsBeforeDependencies()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            new NativeAbiPlanner(null!, NativeAbiTestSupport.ScalarSignatures()));
        Assert.Equal("declarations", error.ParamName);
        var declarations = new RecordingDeclarationValidator();
        var signatures = new RecordingSignaturePlanner();
        var planner = Assert.IsAssignableFrom<INativeAbiPlanner>(
            new NativeAbiPlanner(declarations, signatures));

        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Empty(declarations.Calls);
        Assert.Empty(signatures.Calls);
    }

    [Fact]
    public void ValidatesTheDeclarationBeforePlanningAndPreservesValidationFailures()
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var declarations = new RecordingDeclarationValidator();
        var signatures = new RecordingSignaturePlanner();
        var planner = Assert.IsAssignableFrom<INativeAbiPlanner>(
            new NativeAbiPlanner(declarations, signatures));
        var failure = new CompilerException(new(DiagnosticCode.NativeInterop, "Invalid declaration."));
        declarations.Failure = failure;

        Assert.Same(failure, Assert.Throws<CompilerException>(() => planner.Plan(method)));
        Assert.Same(method, Assert.Single(declarations.Calls));
        Assert.Empty(signatures.Calls);
        declarations.Failure = null;
        var plan = planner.Plan(method);
        Assert.Equal(2, declarations.Calls.Count);
        Assert.Same(method.Definition.NativeImport, plan.Import);
        Assert.Same(signatures.Result, plan.Lowering);
        Assert.Single(signatures.Calls);
    }

    [Fact]
    public void DelegatesOnlyQualifiedDeclarationsAndPreservesPlannerResultsAndFailures()
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var signatures = new RecordingSignaturePlanner();
        var planner = Assert.IsAssignableFrom<INativeAbiPlanner>(new NativeAbiPlanner(new NativeDeclarationValidator(), signatures));

        var plan = planner.Plan(method);

        Assert.Same(signatures.Result, plan.Lowering);
        Assert.Same(method.Definition.NativeImport, plan.Import);
        Assert.Equal((method.Signature, NativeAbiSignatureKind.Import, method.CanonicalName),
            Assert.Single(signatures.Calls));
        Assert.Throws<CompilerException>(() => planner.Plan(method with
        {
            Definition = method.Definition with { IsStatic = false },
        }));
        Assert.Single(signatures.Calls);
        var failure = new CompilerException(new(DiagnosticCode.NativeInterop, "Unsupported signature."));
        signatures.Failure = failure;
        Assert.Same(failure, Assert.Throws<CompilerException>(() => planner.Plan(method)));
        Assert.Equal(2, signatures.Calls.Count);
    }

    private sealed class RecordingSignaturePlanner : INativeAbiSignaturePlanner
    {
        public NativeAbiSignaturePlan Result { get; } = NativeAbiTestSupport.ScalarSignatures().Plan(
            MethodSignatureModel.Create(CliValueKind.NativeInt), NativeAbiSignatureKind.Import, "fixture");
        public CompilerException? Failure { get; set; }
        public List<(MethodSignatureModel Signature, NativeAbiSignatureKind Kind, string Method)> Calls { get; } = [];

        public NativeAbiSignaturePlan Plan(MethodSignatureModel signature, NativeAbiSignatureKind kind, string methodName)
        {
            Calls.Add((signature, kind, methodName));
            return Failure is null ? Result : throw Failure;
        }
    }

    private sealed class RecordingDeclarationValidator : INativeDeclarationValidator
    {
        public List<MethodInstanceModel> Calls { get; } = [];
        public CompilerException? Failure { get; set; }

        public void Validate(MethodInstanceModel method)
        {
            Calls.Add(method);
            if (Failure is not null)
                throw Failure;
        }
    }

    private static void Reject(MethodInstanceModel method)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            ((INativeAbiPlanner)new NativeAbiPlanner(new NativeDeclarationValidator(), NativeAbiTestSupport.ScalarSignatures())).Plan(method));
        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
        Assert.Equal(method.CanonicalName, exception.Diagnostic.Method);
    }
}
