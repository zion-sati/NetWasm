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

    // A [WasmImportLinkage] import keeps its library name as the Wasm import
    // module, so the host supplies it instead of the linked runtime.
    [Fact]
    public void ImportsAWasmImportLinkageDeclarationFromItsLibraryModule()
    {
        var linked = Method(1, "create_node", MethodSignatureModel.Create(CliValueKind.I8, CliValueKind.I4));
        linked = linked with
        {
            Definition = linked.Definition with
            {
                NativeImport = linked.Definition.NativeImport! with { HasWasmImportLinkage = true },
            },
        };
        var planner = Assert.IsAssignableFrom<INativeImportPlanner>(new NativeImportPlanner(new RecordingAbiPlanner()));

        var import = Assert.Single(planner.Plan([linked]).Methods).Import;

        Assert.Equal("mule", import.Module);
        Assert.Equal("create_node", import.Name);
        Assert.Equal(CliValueKind.I8, import.Type.Result);
        Assert.Equal([CliValueKind.I4], import.Type.Parameters.ToArray());
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

    // Declarations of one native symbol bind one function, as on desktop .NET (an IntPtr and a byte*
    // overload, say): they share its import, while every method keeps its own plan.
    [Fact]
    public void DeclarationsOfOneSymbolShareItsImport()
    {
        var pointer = Method(1, "get_buffer", MethodSignatureModel.Create(CliValueKind.NativeInt), "GetBuffer");
        var bytes = Method(2, "get_buffer", MethodSignatureModel.Create(CliValueKind.NativeInt), "GetBufferBytes");
        var other = Method(3, "other", MethodSignatureModel.Create(CliValueKind.Void));

        var result = new NativeImportPlanner(new RecordingAbiPlanner()).Plan([other, bytes, pointer]);

        Assert.Equal([pointer, bytes, other], result.Methods.Select(import => import.Method));
        Assert.Equal(["get_buffer", "other"], result.Imports.Select(import => import.Name));
        Assert.Equal(0, result.ImportPositions[pointer.Definition.Key]);
        Assert.Equal(0, result.ImportPositions[bytes.Definition.Key]);
        Assert.Equal(1, result.ImportPositions[other.Definition.Key]);
    }

    // One Wasm import has one type, so declarations of one symbol must agree on it, in the result and
    // in each parameter.
    [Theory]
    [InlineData(CliValueKind.I8, CliValueKind.I4)]
    [InlineData(CliValueKind.I4, CliValueKind.I8)]
    public void DeclarationsOfOneSymbolWithDifferentSignaturesAreRejected(CliValueKind result, CliValueKind parameter)
    {
        var first = Method(1, "get_buffer", MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4), "GetBuffer");
        var second = Method(2, "get_buffer", MethodSignatureModel.Create(result, parameter), "GetBufferWide");

        var error = Assert.Throws<CompilerException>(() =>
            new NativeImportPlanner(new RecordingAbiPlanner()).Plan([first, second]));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal(second.CanonicalName, error.Diagnostic.Method);
        Assert.Contains($"'{first.CanonicalName}' and '{second.CanonicalName}'", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'get_buffer' with different C ABI signatures", error.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static MethodInstanceModel Method(int token, string entry, MethodSignatureModel signature, string? name = null)
    {
        var assembly = new AssemblyIdentity("Native");
        var definition = new MethodDefinitionModel(new(assembly, token), new(assembly, 100), name ?? entry, true, signature, 0)
        {
            NativeImport = new("mule", entry, MethodImportAttributes.CallingConventionCDecl,
                false, false, null),
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
