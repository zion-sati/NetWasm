// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
            method.Definition with { NativeImport = import with { TranslatesHResult = true } },
            method.Definition with { DisablesRuntimeMarshalling = true, NativeImport = import with { TranslatesHResult = true } },
            method.Definition with { NativeImport = import with { UnsupportedCallingConvention = "calling convention 'x' is not supported" } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | MethodImportAttributes.SetLastError } },
            method.Definition with { NativeImport = import with { Attributes = import.Attributes | unchecked((MethodImportAttributes)0x8000) } },
            method.Definition with { NativeImport = import with { Attributes = 0 } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionFastCall } },
            method.Definition with { NativeImport = import with { Attributes = MethodImportAttributes.CallingConventionMask } },
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

    // An assembly with [DisableRuntimeMarshalling] ignores [MarshalAs], as desktop .NET does; varargs stay
    // unsupported under either rule set.
    [Fact]
    public void DisabledRuntimeMarshallingIgnoresMarshalAs()
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.Void));
        var import = method.Definition.NativeImport!;
        var disabled = method.Definition with { DisablesRuntimeMarshalling = true };

        Validator().Validate(method with { Definition = disabled with { NativeImport = import with { HasMarshalling = true } } });
        Reject(method with { Definition = disabled with { NativeImport = import with { IsVarArg = true } } });
        Assert.Equal(NativeMarshallingRules.Disabled, disabled.NativeMarshalling);
        Assert.Equal(NativeMarshallingRules.Runtime, method.Definition.NativeMarshalling);
    }

    // Each convention desktop .NET's 64-bit platforms call as their C convention.
    [Theory]
    [InlineData(MethodImportAttributes.CallingConventionWinApi)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl)]
    [InlineData(MethodImportAttributes.CallingConventionStdCall)]
    [InlineData(MethodImportAttributes.CallingConventionThisCall)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl | MethodImportAttributes.ExactSpelling)]
    // CharSet, BestFitMapping and ThrowOnUnmappableChar shape only char and string marshalling, which no signature
    // NetWasm accepts has, so desktop .NET calls such an import unchanged.
    [InlineData(MethodImportAttributes.CallingConventionWinApi | MethodImportAttributes.CharSetUnicode)]
    [InlineData(MethodImportAttributes.CallingConventionWinApi | MethodImportAttributes.CharSetAnsi)]
    [InlineData(MethodImportAttributes.CallingConventionWinApi | MethodImportAttributes.CharSetAuto)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl | MethodImportAttributes.BestFitMappingEnable |
        MethodImportAttributes.ThrowOnUnmappableCharEnable)]
    [InlineData(MethodImportAttributes.CallingConventionCDecl | MethodImportAttributes.BestFitMappingDisable |
        MethodImportAttributes.ThrowOnUnmappableCharDisable)]
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

    // Desktop .NET fails FastCall, and the reader's reason for an [UnmanagedCallConv] is reported as given.
    [Theory]
    [InlineData(MethodImportAttributes.CallingConventionFastCall, null, "calling convention 'CallingConventionFastCall' is not supported")]
    [InlineData(MethodImportAttributes.CallingConventionWinApi, "calling convention 'S.CallConvSwift' is not supported", "'S.CallConvSwift'")]
    public void NamesTheConventionItCannotHonor(MethodImportAttributes attributes, string? reason, string named)
    {
        var method = Method(CliTypeIdentity.FromStackKind(CliValueKind.Void));
        var import = method.Definition.NativeImport! with { Attributes = attributes, UnsupportedCallingConvention = reason };

        var error = Assert.Throws<CompilerException>(() => Validator().Validate(
            method with { Definition = method.Definition with { NativeImport = import } }));

        Assert.Contains(named, error.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static MethodInstanceModel Method(CliTypeIdentity result, params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        return new(new(new(Assembly, 0x06000001), new(Assembly, 0x02000001),
            "Call", true, signature, 0)
        {
            NativeImport = new("fixture", "entry", MethodImportAttributes.CallingConventionCDecl,
                false, false, null),
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
