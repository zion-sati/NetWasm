using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeImportPlannerTests
{
    [Fact]
    public void PlansExactRawSignaturesInCanonicalOrderAndCoalescesIdenticalMethods()
    {
        var first = Method(1, "first", MethodSignatureModel.Create(CliValueKind.F8,
            CliValueKind.NativeInt, CliValueKind.I8));
        var second = Method(2, "second", MethodSignatureModel.Create(CliValueKind.Void));
        var abi = new RecordingAbiPlanner();
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(abi));

        var result = planner.Plan([second, first, first with { }]);

        Assert.Equal([first, second], result.Methods.Select(import => import.Method));
        Assert.Equal([second, first], abi.Calls);
        Assert.Equal(2, result.ByMethod.Count);
        Assert.Same(result.Methods[0], result.ByMethod[first.Definition.Key]);
        var raw = result.Methods[0].Import;
        Assert.Equal(RuntimeAbi.RuntimeModule, raw.Module);
        Assert.Equal("first", raw.Name);
        Assert.Equal(CliValueKind.F8, raw.Type.Result);
        Assert.Equal([CliValueKind.NativeInt, CliValueKind.I8], raw.Type.Parameters.ToArray());
        Assert.Equal(first.Definition.NativeImport, result.Methods[0].Abi.Import);
        Assert.Equal(CliValueKind.Void, result.Methods[1].Import.Type.Result);
        Assert.Empty(result.Methods[1].Import.Type.Parameters);
    }

    [Fact]
    public void ManagedAndUnusedDeclarationsDoNotCreateNativeImports()
    {
        var managed = Method(1, "managed", MethodSignatureModel.Create(CliValueKind.I4));
        managed = managed with { Definition = managed.Definition with { NativeImport = null } };
        var abi = new RecordingAbiPlanner();
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(abi));

        Assert.Empty(planner.Plan([]).Methods);
        Assert.Empty(planner.Plan([managed]).Methods);
        Assert.Empty(abi.Calls);
    }

    [Fact]
    public void ConflictingDescriptorFactsRejectWithoutAPlanOrLaterValidation()
    {
        var first = Method(1, "first", MethodSignatureModel.Create(CliValueKind.I4));
        var conflict = first with
        {
            Definition = first.Definition with
            {
                NativeImport = first.Definition.NativeImport! with { EntryPoint = "different" },
            },
        };
        var abi = new RecordingAbiPlanner();
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(abi));

        var error = Assert.Throws<CompilerException>(() => planner.Plan([first, conflict, first]));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Single(abi.Calls);
    }

    [Fact]
    public void AbiFailureIsPreservedAndDoesNotValidateLaterMethods()
    {
        var first = Method(1, "first", MethodSignatureModel.Create(CliValueKind.I4));
        var error = new CompilerException(new(DiagnosticCode.NativeInterop, "Unsupported ABI."));
        var abi = new RecordingAbiPlanner(error);
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(abi));

        Assert.Same(error, Assert.Throws<CompilerException>(() => planner.Plan([first, first])));
        Assert.Single(abi.Calls);
    }

    [Fact]
    public void RequiredPlannerInputsReject()
    {
        Assert.Throws<ArgumentNullException>(() => new NativeImportPlanner(null!));
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(new RecordingAbiPlanner()));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
    }

    private static MethodInstanceModel Method(int token, string entry, MethodSignatureModel signature)
    {
        var assembly = new AssemblyIdentity("Native");
        var definition = new MethodDefinitionModel(new(assembly, token), new(assembly, 100), entry, true, signature, 0)
        {
            NativeImport = new("mule", entry, MethodImportAttributes.CallingConventionCDecl,
                false, false, false, false),
        };
        return new(definition, CliTypeIdentity.Named(assembly, "Test", "Native", false), [], signature);
    }

    private sealed class RecordingAbiPlanner(CompilerException? error = null) : INativeAbiPlanner
    {
        public List<MethodInstanceModel> Calls { get; } = [];

        public NativeAbiPlan Plan(MethodInstanceModel method)
        {
            Calls.Add(method);
            if (error is not null)
                throw error;
            return NativeAbiTestSupport.Plan(method.Definition.NativeImport!, method.Signature, method.Signature);
        }
    }
}
