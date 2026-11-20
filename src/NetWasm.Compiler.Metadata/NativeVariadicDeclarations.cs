// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

// The P/Invokes of one assembly, each read once, and those that call a C variadic function through fixed-signature
// overloads, as SQLitePCLRaw declares sqlite3_config and sqlite3_db_config: the bound declarations of one library's
// entry point, by the native signatures their stubs would call, when the planner finds the function variadic.
// Assemblies without such overloads ask the planner nothing.
internal static class NativeVariadicDeclarations
{
    public static (IReadOnlyDictionary<int, NativeImportDeclaration> Imports, IReadOnlyDictionary<int, NativeVariadicCall> Calls)
        Resolve(MetadataReader reader, INativeImportDeclarationReader imports, INativeImportBinder binder,
            INativeVariadicCallPlanner planner, SignatureTypeProvider signatures)
    {
        var read = new Dictionary<int, NativeImportDeclaration>();
        var declarations = new List<(int Token, NativeImportDeclaration Import, MethodSignatureModel Native)>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (imports.Read(reader, method) is not { } import) continue;
            read[MetadataTokens.GetToken(handle)] = import;
            if (import is not { IsVarArg: false, HasWasmImportLinkage: false } || !binder.Binds(import))
                continue;
            var signature = method.DecodeSignature(signatures, genericContext: null);
            var model = new MethodSignatureModel(signature.ReturnType, signature.ParameterTypes);
            declarations.Add((MetadataTokens.GetToken(handle), import, NativeMarshallingStubSignature.Lower(model) ?? model));
        }
        var calls = new Dictionary<int, NativeVariadicCall>();
        foreach (var group in declarations.GroupBy(declaration => (declaration.Import.LibraryName, declaration.Import.EntryPoint)))
        {
            if (group.Count() < 2 || planner.FixedParameters(group.Key.LibraryName, group.Key.EntryPoint,
                    [.. group.Select(declaration => declaration.Native)]) is not { } fixedParameters)
                continue;
            foreach (var declaration in group) calls[declaration.Token] = new(fixedParameters);
        }
        return (read, calls);
    }

    // The native signature a variadic call's stub calls: the fixed parameters and the buffer's address.
    public static MethodSignatureModel NativeSignature(MethodSignatureModel signature, NativeVariadicCall call)
    {
        var native = NativeMarshallingStubSignature.Lower(signature) ?? signature;
        return new(native.ReturnSignatureType,
            [.. native.ParameterSignatureTypes.Take(call.FixedParameters), CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)]);
    }
}
