using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The assemblies a method names: those defining its own type and type arguments, and every
/// type, field, method and signature its body refers to. What emission and root-map
/// analysis read from metadata about these (whether a type is a value type, a delegate,
/// its base types) is not recorded query by query, so a unit depends on the shapes of these
/// assemblies and of the assemblies they reference.
/// </summary>
internal static class UnitAssemblies
{
    public static void Collect(ManagedMethodBody method, HashSet<string> names)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(names);
        Method(method.Method, names);
        var body = method.Body;
        foreach (var local in body.LocalSignatureTypes) Type(local, names);
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.Entity entity: names.Add(entity.Key.Assembly.Name); break;
                case CilOperand.MethodInstance call: Method(call.Value, names); break;
                case CilOperand.FieldInstance field: Field(field.Value, names); break;
                case CilOperand.TypeIdentity type: Type(type.Value, names); break;
                case CilOperand.ExactType type: Type(type.Value, names); break;
                case CilOperand.CallSite site: Signature(site.Signature, names); break;
                case CilOperand.ArrayInitializerData initializer: Field(initializer.Field, names); break;
                case CilOperand.ReadOnlySpanData span:
                    Field(span.Field, names);
                    Type(span.SpanType, names);
                    Type(span.ElementType, names);
                    break;
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            if (region.CatchType is { } catchType) names.Add(catchType.Assembly.Name);
            Type(region.CatchTypeIdentity, names);
        }
    }

    private static void Method(MethodInstanceModel method, HashSet<string> names)
    {
        names.Add(method.Definition.Key.Assembly.Name);
        Type(method.DeclaringType, names);
        foreach (var argument in method.MethodArguments) Type(argument, names);
        Signature(method.Signature, names);
    }

    private static void Field(FieldInstanceModel field, HashSet<string> names)
    {
        names.Add(field.Definition.Key.Assembly.Name);
        Type(field.DeclaringType, names);
        Type(field.FieldType, names);
    }

    private static void Signature(MethodSignatureModel signature, HashSet<string> names)
    {
        Type(signature.ReturnSignatureType, names);
        foreach (var parameter in signature.ParameterSignatureTypes) Type(parameter, names);
    }

    private static void Type(CliTypeIdentity? type, HashSet<string> names)
    {
        if (type is null) return;
        if (type.Assembly is { } assembly) names.Add(assembly.Name);
        foreach (var argument in type.TypeArguments) Type(argument, names);
        Type(type.ElementType, names);
        Type(type.CustomModifier, names);
        if (type.FunctionPointerSignature is { } pointer) Signature(pointer.Signature, names);
    }
}
