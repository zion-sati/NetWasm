// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests;

// A function is variadic only when its declarations' Wasm signatures disagree, and then takes the fixed parameters the
// linked archive's type gives it: all but its last, a pointer. Each declaration must pass those and return the result,
// and each variadic argument must be an integer, a float or a pointer. Archives are read only for such functions.
public sealed class NativeVariadicCallPlannerTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Float32 = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity Struct = CliTypeIdentity.Named(new AssemblyIdentity("a"), "", "S", true);

    [Fact]
    public void DisagreeingDeclarationsTakeTheArchivesFixedParameters()
    {
        var archives = new Archives(("config", Type([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4)));
        var planner = new NativeVariadicCallPlanner(archives, [("other", "other.a"), ("mule", "extra.a"), ("mule", "mule.a")],
            wide: false);

        Assert.Equal(1, planner.FixedParameters("mule", "config",
            [Signature(Int32, Int32), Signature(Int32, Int32, Int32), Signature(Int32, Int32, NativeInt, Int64, Float32, Float64)]));
        Assert.Equal(1, planner.FixedParameters("mule", "config", [Signature(Int32, Int32), Signature(Int32, Int32, Int64)]));
        // Only the library's archives are searched, in order, and each is read once.
        Assert.Equal(["extra.a", "mule.a"], archives.Reads);
    }

    [Fact]
    public void AgreeingDeclarationsAreNotVariadicAndReadNoArchive()
    {
        var archives = new Archives(("config", Type([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4)));
        var planner = new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: false);

        // int and uint pass the same way.
        Assert.Null(planner.FixedParameters("mule", "config", [Signature(Int32, Int32, Int32),
            Signature(Int32, Int32, CliTypeIdentity.Primitive("u4", CliValueKind.I4))]));
        Assert.Null(planner.FixedParameters("mule", "config", []));
        Assert.Empty(archives.Reads);
    }

    // A non-variadic g(int, void*) declared with an int in one place and an IntPtr in another: every declaration
    // passes the type's own two parameters, so it is not shown variadic, on wasm32 (where both are 32 bits) or not.
    [Fact]
    public void InconsistentDeclarationsOfTheTypesOwnArityAreNotVariadic()
    {
        var archives = new Archives(("config", Type([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4)));
        var planner = new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: false);

        Assert.Null(planner.FixedParameters("mule", "config", [Signature(Int32, Int32, Int32), Signature(Int32, Int32, NativeInt)]));
        Assert.Equal(1, planner.FixedParameters("mule", "config",
            [Signature(Int32, Int32, Int32), Signature(Int32, Int32, NativeInt, NativeInt)]));
    }

    // A bitcode module's type says whether the function is variadic: a variadic one's parameters are its fixed ones
    // (a pointer the target's width), and a function it calls not variadic is not, whatever its declarations say.
    [Fact]
    public void BitcodeSaysWhetherAFunctionIsVariadic()
    {
        var archives = new Archives(
            ("config", Type([CliValueKind.NativeInt, CliValueKind.I4], CliValueKind.NativeInt) with { Variadic = true }),
            ("fixed", Type([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4) with { Variadic = false }));
        var narrow = new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: false);
        var wide = new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: true);

        // Every declaration passes the fixed parameters' own count plus one: no count rule applies to a known type.
        Assert.Equal(2, narrow.FixedParameters("mule", "config", [Signature(NativeInt, NativeInt, Int32, Int32),
            Signature(NativeInt, NativeInt, Int32, NativeInt)]));
        Assert.Equal(2, wide.FixedParameters("mule", "config", [Signature(NativeInt, NativeInt, Int32, Int32),
            Signature(NativeInt, NativeInt, Int32, NativeInt)]));
        Assert.Null(narrow.FixedParameters("mule", "fixed", [Signature(Int32, Int32), Signature(Int32, Int32, Int32)]));
    }

    [Fact]
    public void Wasm64PointersAre64Bits()
    {
        var archives = new Archives(("config", Type([CliValueKind.I8, CliValueKind.I4, CliValueKind.I8], CliValueKind.Void)));
        var planner = new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: true);

        Assert.Equal(2, planner.FixedParameters("mule", "config", [Signature(Void, NativeInt, Int32),
            Signature(Void, NativeInt, Int32, NativeInt)]));
        Assert.Null(new NativeVariadicCallPlanner(archives, [("mule", "mule.a")], wide: false).FixedParameters("mule", "config",
            [Signature(Void, NativeInt, Int32), Signature(Void, NativeInt, Int32, NativeInt)]));
    }

    public static TheoryData<string> Rejected => new()
    {
        "no archive type", "other library", "last not a pointer", "no parameters", "fixed differs", "too few", "result differs",
        "struct argument", "reference argument",
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void WhatTheArchiveOrTheDeclarationsDoNotSupportIsNotVariadic(string problem)
    {
        var type = problem switch
        {
            "last not a pointer" => Type([CliValueKind.I4, CliValueKind.F8], CliValueKind.I4),
            "no parameters" => Type([], CliValueKind.I4),
            _ => Type([CliValueKind.I4, CliValueKind.I4], CliValueKind.I4),
        };
        var archives = new Archives((problem == "no archive type" ? "elsewhere" : "config", type));
        var planner = new NativeVariadicCallPlanner(archives, [(problem == "other library" ? "other" : "mule", "mule.a")], wide: false);
        MethodSignatureModel[] declarations = problem switch
        {
            "fixed differs" => [Signature(Int32, Int64), Signature(Int32, Int64, Int32)],
            "too few" => [Signature(Int32), Signature(Int32, Int32, Int32)],
            "result differs" => [Signature(Int64, Int32), Signature(Int64, Int32, Int32)],
            "struct argument" => [Signature(Int32, Int32), Signature(Int32, Int32, Struct)],
            "reference argument" => [Signature(Int32, Int32), Signature(Int32, Int32, CliTypeIdentity.ManagedByReference(Int32))],
            _ => [Signature(Int32, Int32), Signature(Int32, Int32, Int32)],
        };

        Assert.Null(planner.FixedParameters("mule", "config", declarations));
    }

    [Fact]
    public void NoLinkingFindsNothingVariadic()
    {
        var none = NoNativeVariadicCalls.Instance;
        Assert.Null(none.FixedParameters("mule", "config", [Signature(Int32, Int32), Signature(Int32)]));
        Assert.Throws<ArgumentNullException>(() => none.FixedParameters(null!, "e", []));
        Assert.Throws<ArgumentNullException>(() => none.FixedParameters("l", null!, []));
        Assert.Throws<ArgumentNullException>(() => none.FixedParameters("l", "e", null!));
        var planner = new NativeVariadicCallPlanner(new Archives(), [], wide: false);
        Assert.Throws<ArgumentNullException>(() => planner.FixedParameters(null!, "e", []));
        Assert.Throws<ArgumentNullException>(() => planner.FixedParameters("l", null!, []));
        Assert.Throws<ArgumentNullException>(() => planner.FixedParameters("l", "e", null!));
    }

    [Fact]
    public void FunctionTypesCompareByValue()
    {
        Assert.Equal(Type([CliValueKind.I4], CliValueKind.Void), Type([CliValueKind.I4], CliValueKind.Void));
        Assert.Equal(Type([CliValueKind.I4], CliValueKind.Void).GetHashCode(), Type([CliValueKind.I4], CliValueKind.Void).GetHashCode());
        Assert.NotEqual(Type([CliValueKind.I4], CliValueKind.Void), Type([CliValueKind.I8], CliValueKind.Void));
        Assert.NotEqual(Type([CliValueKind.I4], CliValueKind.Void), Type([CliValueKind.I4], CliValueKind.I4));
        Assert.NotEqual(Type([CliValueKind.I4], CliValueKind.Void), Type([CliValueKind.I4], CliValueKind.Void) with { Variadic = true });
        Assert.False(Type([], CliValueKind.Void).Equals(null));
    }

    private static NativeFunctionType Type(CliValueKind[] parameters, CliValueKind result) => new([.. parameters], result);

    private static MethodSignatureModel Signature(CliTypeIdentity result, params CliTypeIdentity[] parameters) =>
        new(result, [.. parameters]);

    private sealed class Archives(params (string Name, NativeFunctionType Type)[] functions) : INativeArchiveFunctionTypeReader
    {
        public List<string> Reads { get; } = [];

        public ImmutableDictionary<string, NativeFunctionType> Read(string archivePath)
        {
            Reads.Add(archivePath);
            return archivePath == "mule.a"
                ? functions.ToImmutableDictionary(function => function.Name, function => function.Type)
                : ImmutableDictionary<string, NativeFunctionType>.Empty;
        }
    }
}
