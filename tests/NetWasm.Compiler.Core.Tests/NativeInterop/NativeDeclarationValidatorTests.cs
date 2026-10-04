using System.Reflection;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeDeclarationValidatorTests
{
    private static readonly AssemblyIdentity Assembly = new("NativeFixture");
    private static readonly CliTypeIdentity DeclaringType = CliTypeIdentity.Named(
        Assembly, "Fixture", "NativeCalls", isValueType: false);

    [Fact]
    public void RejectsUnsupportedDeclarationFactsAndKeepsTheNativeDiagnostic()
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.Void));
        var import = method.Definition.NativeImport!;
        foreach (var invalid in new[]
        {
            method.Definition with { NativeImport = null },
            method.Definition with { IsStatic = false },
            method.Definition with { IsAbstract = true },
            method.Definition with { RelativeVirtualAddress = 1 },
            method.Definition with { GenericArity = 1 },
            method.Definition with { NativeImport = import with { LibraryName = "" } },
            method.Definition with { NativeImport = import with { EntryPoint = " " } },
            method.Definition with { NativeImport = import with { LibraryName = "x\0y" } },
            method.Definition with { NativeImport = import with { EntryPoint = "x\0y" } },
            method.Definition with { NativeImport = import with { IsVarArg = true } },
            method.Definition with { NativeImport = import with { HasMarshalling = true } },
            method.Definition with { NativeImport = import with { SuppressesGcTransition = true } },
            method.Definition with { NativeImport = import with { HasCustomCallingConvention = true } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionStdCall } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.SetLastError } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | unchecked((MethodImportAttributes)0x8000) } },
            method.Definition with { NativeImport = import with { Attributes = 0 } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.CharSetUnicode } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.BestFitMappingEnable } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.ThrowOnUnmappableCharEnable } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.CharSetAnsi } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.CharSetAuto } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionThisCall } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionFastCall } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionMask } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.BestFitMappingDisable } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.ThrowOnUnmappableCharDisable } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.ExactSpelling },JSImport = new("host", null) },
            method.Definition with { JSExport = new("host") },
            method.Definition with { WitImport = new("world", "entry") },
            method.Definition with { WitExport = new("world", "entry") },
            method.Definition with { WitPostReturn = new("world", "entry") },
        })
        {
            Reject(method with { Definition = invalid });
        }
        Reject(method with { MethodArguments = [CliTypeIdentity.FromStackKind(CliValueKind.I4)] });
        Reject(method with { DeclaringType = CliTypeIdentity.GenericParameter(false, 0) });
        Reject(method with
        {
            DeclaringType = CliTypeIdentity.GenericInstantiation(DeclaringType,
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)])
        });
        Assert.Throws<ArgumentNullException>(() => Validator().Validate(null!));
    }

    [Theory]
    [InlineData(MethodImportAttributes.CallingConventionWinApi)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl | MethodImportAttributes.ExactSpelling)]
    public void AcceptsOnlyExplicitQualifiedImportSemantics(MethodImportAttributes attributes)
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.Void));
        method = method with
        {
            Definition = method.Definition with
            {
                NativeImport = method.Definition.NativeImport! with { Attributes = attributes },
            }
        };
        Validator().Validate(method);
        Assert.Equal(attributes, method.Definition.NativeImport!.Attributes);
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
    public void ValidatesDeclarationsWithoutProjectingAggregateSignatures()
    {
        var aggregate = CliTypeIdentity.Named(Assembly, "Fixture", "Pair", isValueType: true);
        var method = Method(aggregate, aggregate);

        Validator().Validate(method);

        Assert.Equal(aggregate, method.Signature.ReturnSignatureType);
        Assert.Equal(aggregate, Assert.Single(method.Signature.ParameterSignatureTypes));
    }

    private static INativeDeclarationValidator Validator() =>
        Assert.IsAssignableFrom<INativeDeclarationValidator>(new NativeDeclarationValidator());

    private static void Reject(MethodInstanceModel method)
    {
        var error = Assert.Throws<CompilerException>(() => Validator().Validate(method));
        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal(method.CanonicalName, error.Diagnostic.Method);
    }
}
