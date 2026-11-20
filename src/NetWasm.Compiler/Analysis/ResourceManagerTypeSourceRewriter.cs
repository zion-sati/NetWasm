// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler.Analysis;

// new ResourceManager(typeof(T)), which resource classes generated from .resx files construct, names the resources
// after T: its namespace, a dot, then its name; a nested type's namespace is its outermost declaring type's. A NetWasm
// program keeps no type names at run time, so with T known here the three instructions become T's handle, that name
// and ResourceManager's internal (RuntimeTypeHandle, string) constructor, which takes T's assembly from the handle.
// Any other Type reaches ResourceManager(Type), which throws.
internal sealed class ResourceManagerTypeSourceRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private const string ResourceManager = "System.Resources.ResourceManager";

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        CilInstruction[]? instructions = null;
        for (var index = 2; index < body.Instructions.Length; index++)
        {
            var construction = body.Instructions[index];
            var typeOf = body.Instructions[index - 1];
            var token = body.Instructions[index - 2];
            if (token is not { Operation: CilOperation.LoadTypeToken, Operand: CilOperand.TypeIdentity { Value: var source } } ||
                source.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
                !IsTypeFromHandle(typeOf) ||
                TypeSourceConstructor(construction) is not { } typeSource ||
                Constructor(typeSource) is not { } constructor)
            {
                continue;
            }
            instructions ??= body.Instructions.ToArray();
            instructions[index - 1] = typeOf with { Operation = CilOperation.LoadString, Operand = new CilOperand.UserString(BaseName(source)) };
            instructions[index] = construction with { Operand = new CilOperand.MethodInstance(constructor) };
        }
        // The handle and the name are on the stack before the constructor, one more value than the Type was.
        return instructions is null ? body : body with { Instructions = [.. instructions], MaxStack = body.MaxStack + 1 };
    }

    private bool IsTypeFromHandle(CilInstruction instruction) =>
        instruction.Operation == CilOperation.Call &&
        calledMethods.Resolve(instruction) is { Definition: { Name: "GetTypeFromHandle", IsStatic: true } } method &&
        symbols.Format(method.Definition.DeclaringType) == "System.Type";

    private MethodInstanceModel? TypeSourceConstructor(CilInstruction instruction) =>
        instruction.Operation == CilOperation.NewObject &&
        calledMethods.Resolve(instruction) is { Definition: { Name: ".ctor", IsStatic: false } } constructor &&
        constructor.Signature.ParameterSignatureTypes is [{ FullName: "System.Type" }] &&
        symbols.Format(constructor.Definition.DeclaringType) == ResourceManager
            ? constructor
            : null;

    private MethodInstanceModel? Constructor(MethodInstanceModel typeSource)
    {
        var constructor = typeDefinitions.ResolveTypeIdentity(typeSource.DeclaringType).Methods
            .Select(methods.GetMethod)
            .SingleOrDefault(method => method.Name == ".ctor" && !method.IsStatic &&
                method.Signature.ParameterSignatureTypes is [{ FullName: "System.RuntimeTypeHandle" }, { CanonicalName: "primitive:string" }]);
        return constructor is null ? null : methodInstances.ResolveMethodInstance(
            constructor.Key.Assembly, constructor.Key.MetadataToken, symbols.Format(constructor), 0);
    }

    // Upstream's ResourceManager(Type): Type.Namespace, a dot, then Type.Name, as the program's type names give them.
    private string BaseName(CliTypeIdentity source)
    {
        var (name, @namespace) = RuntimeTypeNameFormatter.NameAndNamespace(source, typeDefinitions);
        return @namespace is null ? name : $"{@namespace}.{name}";
    }
}
