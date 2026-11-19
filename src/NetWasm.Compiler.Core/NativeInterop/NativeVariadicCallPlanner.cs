// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core.NativeInterop;

// A C variadic function's Wasm type is its fixed parameters and one pointer, to the buffer that holds the variadic
// arguments; nothing in the type says the function is variadic. Declarations that disagree on their parameters' kinds
// (C has no overloads) and pass different numbers of them than the type has show it is: SQLitePCLRaw declares
// sqlite3_config with one to ten. Declarations that all pass the type's own number of parameters are a non-variadic
// function declared inconsistently, which the import planner names. The type comes from the archive that links the
// library: its fixed parameters are all but the last, which is a pointer, and every declaration must pass those and
// return its result. Each variadic argument
// must be a value C's default promotions apply to (an integer, a float or a pointer); a struct or a reference is not.
// Archives are read once each, when a declaration first needs them.
public sealed class NativeVariadicCallPlanner(
    INativeArchiveFunctionTypeReader archives,
    ImmutableArray<(string Library, string Path)> linked,
    bool wide) : INativeVariadicCallPlanner
{
    private readonly ConcurrentDictionary<string, ImmutableDictionary<string, NativeFunctionType>> _types =
        new(StringComparer.Ordinal);

    public int? FixedParameters(string libraryName, string entryPoint,
        IReadOnlyCollection<MethodSignatureModel> declarations)
    {
        ArgumentNullException.ThrowIfNull(libraryName);
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(declarations);
        var pointer = wide ? CliValueKind.I8 : CliValueKind.I4;
        var shapes = declarations.Select(declaration => declaration.ParameterSignatureTypes
            .Select(parameter => Kind(parameter, CliValueKind.NativeInt))
            .Append(Kind(declaration.ReturnSignatureType, CliValueKind.NativeInt)).ToImmutableArray()).ToArray();
        if (shapes.Length == 0 || shapes.Skip(1).All(shape => shape.SequenceEqual(shapes[0]))) return null;
        var type = linked.Where(archive => archive.Library == libraryName)
            .Select(archive => _types.GetOrAdd(archive.Path, archives.Read).GetValueOrDefault(entryPoint))
            .FirstOrDefault(found => found is not null);
        // A bitcode module's type says whether the function is variadic, and its parameters are the fixed ones; a Wasm
        // object's ends in the variadic arguments' pointer.
        ImmutableArray<CliValueKind> fixedKinds;
        switch (type)
        {
            case { Variadic: true }:
                fixedKinds = [.. type.Parameters.Select(kind => kind == CliValueKind.NativeInt ? pointer : kind)];
                break;
            case { Variadic: null, Parameters: [.. var objectFixed, var last] } when last == pointer &&
                declarations.Any(declaration => declaration.ParameterSignatureTypes.Length != type.Parameters.Length):
                fixedKinds = objectFixed;
                break;
            default:
                return null;
        }
        var result = type.Result == CliValueKind.NativeInt ? pointer : type.Result;
        foreach (var declaration in declarations)
        {
            var parameters = declaration.ParameterSignatureTypes;
            if (parameters.Length < fixedKinds.Length || Kind(declaration.ReturnSignatureType, pointer) != result ||
                !parameters[..fixedKinds.Length].Select(parameter => Kind(parameter, pointer))
                    .SequenceEqual(fixedKinds.Select(kind => (CliValueKind?)kind)) ||
                parameters[fixedKinds.Length..].Any(parameter => Kind(parameter, pointer) is not
                    (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8)))
                return null;
        }
        return fixedKinds.Length;
    }

    // The Wasm value kind a native signature's type passes as, a native integer as the pointer kind given, or null for
    // one with none.
    private static CliValueKind? Kind(CliTypeIdentity type, CliValueKind pointer) => type.StackKind switch
    {
        CliValueKind.Void => CliValueKind.Void,
        CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 => type.StackKind,
        CliValueKind.NativeInt => pointer,
        _ => null,
    };
}
