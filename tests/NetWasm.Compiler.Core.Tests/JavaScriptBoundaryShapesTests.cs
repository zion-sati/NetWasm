// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// The Invoke signatures of Action and Func, which .NET's JavaScript binding generator accepts as delegate parameters,
// and an import whose [JSMarshalAs] does not fit its parameter.
public sealed class JavaScriptBoundaryShapesTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);

    public static TheoryData<CliTypeIdentity, CliValueKind?, int> Delegates() => new()
    {
        { Named("System", "Action"), CliValueKind.Void, 0 },
        { Generic("Action`1", Int32), CliValueKind.Void, 1 },
        { Generic("Func`1", Int32), CliValueKind.I4, 0 },
        { Generic("Func`2", Int32, Int64), CliValueKind.I8, 1 },
        // Another generic type, another named type, and a type that is neither.
        { Generic("Predicate`1", Int32), null, 0 },
        { Named("System", "String"), null, 0 },
        { CliTypeIdentity.SzArray(Int32), null, 0 },
    };

    [Theory]
    [MemberData(nameof(Delegates))]
    public void StandardDelegatesHaveTheirInvokeSignature(CliTypeIdentity type, CliValueKind? result, int parameters)
    {
        var invoke = JavaScriptBoundaryShapes.StandardDelegateInvoke(type);

        Assert.Equal(result, invoke?.ReturnType);
        if (invoke is not null) Assert.Equal(parameters, invoke.ParameterTypes.Length);
    }

    [Fact]
    public void AnImportWhoseMarshalAsDoesNotFitItsParameterIsUnsupported()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4) with { ParameterSignatureTypes = [Int32] };

        var unsupported = JavaScriptBoundaryShapes.UnsupportedImport(signature, new JavaScriptMarshalAs(["String"], null), false,
            JavaScriptBoundaryShapes.StandardDelegateInvoke);

        Assert.StartsWith("JSMarshalAs<", unsupported);
    }

    private static CliTypeIdentity Named(string @namespace, string name) => CliTypeIdentity.Named(Assembly, @namespace, name, false);

    private static CliTypeIdentity Generic(string name, params CliTypeIdentity[] arguments) =>
        CliTypeIdentity.GenericInstantiation(Named("System", name), [.. arguments]);
}
