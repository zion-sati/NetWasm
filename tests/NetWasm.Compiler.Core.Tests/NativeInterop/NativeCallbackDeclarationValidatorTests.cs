using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeCallbackDeclarationValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsDefaultAndCdeclStaticAddresses(bool cdecl)
    {
        var method = Callback(cdecl
            ? ["System.Runtime.CompilerServices.CallConvCdecl"]
            : []);

        new NativeCallbackDeclarationValidator().ValidateAddressTarget(method);
    }

    [Fact]
    public void AcceptsNamedEntryPoint()
    {
        var method = Callback([]);
        method = method with
        {
            Definition = method.Definition with
            {
                NativeCallback = method.Definition.NativeCallback! with
                {
                    EntryPoint = "native_entry",
                },
            },
        };

        new NativeCallbackDeclarationValidator().ValidateAddressTarget(method);
    }

    [Fact]
    public void RejectsUnsupportedDeclarationAndMethodShapes()
    {
        var baseline = Callback([]);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var genericType = CliTypeIdentity.Named(
            new AssemblyIdentity("Callbacks"),
            "Fixture",
            "Callbacks`1",
            isValueType: false);
        foreach (var method in new[]
        {
            baseline with { Definition = baseline.Definition with { NativeCallback = null } },
            baseline with { Definition = baseline.Definition with { IsStatic = false } },
            baseline with { Definition = baseline.Definition with { IsAbstract = true } },
            baseline with { Definition = baseline.Definition with { RelativeVirtualAddress = 0 } },
            baseline with { Definition = baseline.Definition with { GenericArity = 1 } },
            baseline with
            {
                DeclaringType = CliTypeIdentity.GenericParameter(method: false, 0),
            },
            baseline with
            {
                DeclaringType = CliTypeIdentity.GenericInstantiation(genericType, [integer]),
            },
            baseline with { MethodArguments = [integer] },
            baseline with { Definition = baseline.Definition with
            {
                NativeImport = new(
                    "fixture",
                    "entry",
                    System.Reflection.MethodImportAttributes.CallingConventionCDecl,
                    false,
                    false,
                    false,
                    false),
            } },
            baseline with { Definition = baseline.Definition with
            {
                JSImport = new("callback", "module"),
            } },
            baseline with { Definition = baseline.Definition with
            {
                JSExport = new("callback"),
            } },
            baseline with { Definition = baseline.Definition with
            {
                WitImport = new("fixture:callbacks/api", "callback"),
            } },
            baseline with { Definition = baseline.Definition with
            {
                WitExport = new("fixture:callbacks/api", "callback"),
            } },
            baseline with { Definition = baseline.Definition with
            {
                WitPostReturn = new("fixture:callbacks/api", "callback"),
            } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with { EntryPoint = " " } } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with { EntryPoint = "bad\nname" } } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with { IsVarArg = true } } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with {
                    CallingConventions = ["System.Runtime.CompilerServices.CallConvStdcall"] } } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with {
                    CallingConventions = [
                        "System.Runtime.CompilerServices.CallConvCdecl",
                        "System.Runtime.CompilerServices.CallConvSuppressGCTransition"] } } },
            baseline with { Definition = baseline.Definition with { NativeCallback =
                baseline.Definition.NativeCallback! with { HasUnsupportedNamedArguments = true } } },
        })
        {
            var exception = Assert.Throws<CompilerException>(() =>
                new NativeCallbackDeclarationValidator().ValidateAddressTarget(method));
            Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
            Assert.Equal(method.CanonicalName, exception.Diagnostic.Method);
        }
    }

    [Fact]
    public void RejectsNullMethod()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NativeCallbackDeclarationValidator().ValidateAddressTarget(null!));
    }

    private static MethodInstanceModel Callback(string[] conventions)
    {
        var assembly = new AssemblyIdentity("Callbacks");
        var declaring = CliTypeIdentity.Named(
            assembly,
            "Fixture",
            "Callbacks",
            isValueType: false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            new(assembly, 1),
            new(assembly, 2),
            "Callback",
            true,
            signature,
            1)
        {
            NativeCallback = new([.. conventions], null, false, false),
        };
        return new(definition, declaring, [], signature);
    }
}
