using System.Reflection;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests;

public sealed class FunctionPointerIdentityTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity Modifier = CliTypeIdentity.Named(new("System.Runtime"),
        "System.Runtime.CompilerServices", "CallConvCdecl", false);

    [Fact]
    public void DistinctFunctionPointerFactsHaveDistinctStructuralIdentities()
    {
        var signature = new CliFunctionPointerSignature(1, 0, 1, new(Int32, [Int32]));
        var pointer = CliTypeIdentity.FunctionPointer(signature);
        Assert.Equal(pointer, CliTypeIdentity.FunctionPointer(signature with { }));
        Assert.False(pointer.ContainsGenericParameters);
        foreach (var other in new[]
        {
            signature with { Header = 0 },
            signature with { GenericArity = 1 },
            signature with { RequiredParameterCount = 0 },
            signature with { Signature = new(Int64, [Int32]) },
            signature with { Signature = new(Int32, [Int64]) },
            signature with { Signature = new(CliTypeIdentity.Modified(Int32, Modifier, false), [Int32]) },
        })
            Assert.NotEqual(pointer, CliTypeIdentity.FunctionPointer(other));
        Assert.NotEqual(pointer, CliTypeIdentity.UnmanagedPointer(Int32));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubstitutionPreservesPointerConventionAndModifierFacts(bool isRequired)
    {
        var typeParameter = CliTypeIdentity.GenericParameter(false, 0);
        var methodParameter = CliTypeIdentity.GenericParameter(true, 0);
        var signature = new CliFunctionPointerSignature(9, 0, 1, new(
            CliTypeIdentity.Modified(typeParameter, Modifier, isRequired), [methodParameter]));
        var pointer = CliTypeIdentity.FunctionPointer(signature);
        Assert.True(pointer.ContainsGenericParameters);
        var closed = pointer.Substitute([Int32], [Int64]);
        Assert.False(closed.ContainsGenericParameters);
        Assert.Equal((byte)9, closed.FunctionPointerSignature!.Header);
        Assert.Equal(1, closed.FunctionPointerSignature.RequiredParameterCount);
        Assert.Equal(Int32, closed.FunctionPointerSignature.Signature.ReturnSignatureType.ElementType);
        Assert.Equal(Modifier, closed.FunctionPointerSignature.Signature.ReturnSignatureType.CustomModifier);
        Assert.Equal(isRequired, closed.FunctionPointerSignature.Signature.ReturnSignatureType.IsRequiredModifier);
        Assert.Equal(Int64, Assert.Single(closed.FunctionPointerSignature.Signature.ParameterSignatureTypes));
        var parameterOnly = CliTypeIdentity.FunctionPointer(new(0, 0, 1, new(Int32, [methodParameter])));
        Assert.True(parameterOnly.ContainsGenericParameters);
        Assert.False(parameterOnly.Substitute([], [Int64]).ContainsGenericParameters);
    }

    [Fact]
    public void StackStorageAdaptationDoesNotEraseFunctionPointerOrModifierFacts()
    {
        var modified = CliTypeIdentity.Modified(Int32, Modifier, false);
        var pointer = CliTypeIdentity.FunctionPointer(new(1, 0, 0, new(modified, [])));
        var changed = pointer.WithStackKind(CliValueKind.I8);
        Assert.Equal(pointer, changed);
        Assert.Same(pointer.FunctionPointerSignature, changed.FunctionPointerSignature);
        Assert.Same(pointer.FunctionPointerSignature, pointer.WithStackStorageType(Int32).FunctionPointerSignature);
        Assert.Same(Modifier, modified.WithStackKind(CliValueKind.I8).CustomModifier);
        Assert.Same(Modifier, modified.WithStackStorageType(Int64).CustomModifier);
        Assert.NotEqual(modified, CliTypeIdentity.Modified(Int32, Modifier, true));
    }

    [Fact]
    public void InvalidSignatureAndModifierArgumentsRejectBeforeIdentityConstruction()
    {
        var signature = new CliFunctionPointerSignature(1, 0, 0, new(Int32, []));
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.FunctionPointer(null!));
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.FunctionPointer(signature with { Signature = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CliTypeIdentity.FunctionPointer(signature with { GenericArity = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CliTypeIdentity.FunctionPointer(signature with { RequiredParameterCount = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CliTypeIdentity.FunctionPointer(signature with { RequiredParameterCount = 1 }));
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.Modified(null!, Modifier, false));
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.Modified(Int32, null!, false));
    }

    [Fact]
    public void DescriptorEquivalenceIncludesFunctionPointerSignatures()
    {
        var assembly = new AssemblyIdentity("Pointers");
        var declaring = CliTypeIdentity.Named(assembly, "Example", "Methods", false);
        var pointer = CliTypeIdentity.FunctionPointer(new(1, 0, 0, new(Int32, [])));
        var signature = new MethodSignatureModel(pointer, []);
        var definition = new MethodDefinitionModel(new(assembly, 1), new(assembly, 2), "Get", true, signature, 0);
        var method = new MethodInstanceModel(definition, declaring, [], signature);
        Assert.True(method.HasEquivalentDescriptorFacts(method with { }));
        var different = new MethodSignatureModel(CliTypeIdentity.FunctionPointer(
            pointer.FunctionPointerSignature! with { Header = 0 }), []);
        Assert.False(method.HasEquivalentDescriptorFacts(method with { Signature = different }));
        Assert.False(method.HasEquivalentDescriptorFacts(method with { Definition = definition with { Signature = different } }));
    }

    [Fact]
    public void DescriptorEquivalenceRequiresEveryNativeDeclarationFactToAgree()
    {
        var assembly = new AssemblyIdentity("NativeDescriptors");
        var declaring = CliTypeIdentity.Named(assembly, "Example", "Methods", false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.NativeInt);
        var declaration = new NativeImportDeclaration("library", "entry", MethodImportAttributes.CallingConventionCDecl,
            false, false, false, false);
        var definition = new MethodDefinitionModel(new(assembly, 1), new(assembly, 2), "Call", true, signature, 0)
        {
            NativeImport = declaration,
        };
        var method = new MethodInstanceModel(definition, declaring, [], signature);

        Assert.True(method.HasEquivalentDescriptorFacts(method with
        {
            Definition = definition with { NativeImport = declaration with { } },
        }));
        foreach (var changed in new NativeImportDeclaration?[]
        {
            null,
            declaration with { LibraryName = "other" },
            declaration with { EntryPoint = "other" },
            declaration with { Attributes = MethodImportAttributes.CallingConventionWinApi },
            declaration with { IsVarArg = true },
            declaration with { HasMarshalling = true },
            declaration with { SuppressesGcTransition = true },
            declaration with { HasCustomCallingConvention = true },
        })
        {
            Assert.False(method.HasEquivalentDescriptorFacts(method with
            {
                Definition = definition with { NativeImport = changed },
            }));
        }
    }

    [Fact]
    public void DescriptorEquivalenceRequiresEveryNativeCallbackFactToAgree()
    {
        var assembly = new AssemblyIdentity("CallbackDescriptors");
        var declaring = CliTypeIdentity.Named(assembly, "Example", "Methods", false);
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var declaration = new NativeCallbackDeclaration(
            ["System.Runtime.CompilerServices.CallConvCdecl"],
            null,
            IsVarArg: false,
            HasUnsupportedNamedArguments: false);
        var definition = new MethodDefinitionModel(
            new(assembly, 1),
            new(assembly, 2),
            "Callback",
            true,
            signature,
            1)
        {
            NativeCallback = declaration,
        };
        var method = new MethodInstanceModel(definition, declaring, [], signature);

        Assert.True(method.HasEquivalentDescriptorFacts(method with
        {
            Definition = definition with
            {
                NativeCallback = declaration with { CallingConventions = [.. declaration.CallingConventions] },
            },
        }));
        foreach (var changed in new NativeCallbackDeclaration?[]
        {
            null,
            declaration with { CallingConventions = [] },
            declaration with { EntryPoint = "named" },
            declaration with { IsVarArg = true },
            declaration with { HasUnsupportedNamedArguments = true },
        })
        {
            Assert.False(method.HasEquivalentDescriptorFacts(method with
            {
                Definition = definition with { NativeCallback = changed },
            }));
        }
    }
}
