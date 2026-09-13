using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Wit.Bindings.FlatSlots;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings;

public abstract class WitBindingMethodSectionBase(
    IWitCanonicalTypeResolver types,
    ICanonicalAbiTypeFlattener flattener,
    ICanonicalAbiMemoryLayoutPlanner layouts,
    IWitBindingFunctionModelBuilder models,
    IWitBindingSyntaxFormatter syntax,
    IWitFlatSlotCoercionFormatter slotCoercions)
{
    private readonly IWitCanonicalTypeResolver _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ICanonicalAbiTypeFlattener _flattener = flattener ??
        throw new ArgumentNullException(nameof(flattener));
    private readonly ICanonicalAbiMemoryLayoutPlanner _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IWitBindingFunctionModelBuilder _models = models ??
        throw new ArgumentNullException(nameof(models));
    private readonly IWitBindingSyntaxFormatter _syntax = syntax ??
        throw new ArgumentNullException(nameof(syntax));
    private readonly IWitFlatSlotCoercionFormatter _slotCoercions = slotCoercions ??
        throw new ArgumentNullException(nameof(slotCoercions));

    protected void WriteImports(
        CodeWriter writer,
        WitDocument document,
        WitWorld world,
        FlatBindingTypeSet typeSet)
    {
        foreach (var item in world.Imports)
        {
            if (item.InterfaceId is int interfaceId)
            {
                var @interface = document.Interfaces[interfaceId];
                WriteImportClass(
                    writer,
                    document,
                    _syntax.Format(new WitBindingSyntaxRequest.Identifier(@interface.Name)) + "Imports",
                    $"{@interface.Package}/{@interface.Name}",
                    @interface.Functions,
                    typeSet);
            }
            else
            {
                WriteImportClass(
                    writer,
                    document,
                    _syntax.Format(new WitBindingSyntaxRequest.Identifier(world.Name)) + "Imports",
                    string.Empty,
                    [item.Function!],
                    typeSet);
            }
        }
    }

    private void WriteImportClass(
        CodeWriter writer,
        WitDocument document,
        string className,
        string interfaceName,
        IEnumerable<WitFunction> functions,
        FlatBindingTypeSet typeSet)
    {
        writer.Line($"public static class {className}");
        writer.Line("{");
        writer.Indent();
        foreach (var function in functions)
        {
            WriteImport(writer, document, interfaceName, function, typeSet);
            writer.Line();
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
    }

    private void WriteImport(
        CodeWriter writer,
        WitDocument document,
        string interfaceName,
        WitFunction function,
        FlatBindingTypeSet typeSet)
    {
        var model = Model(
            document,
            interfaceName,
            function,
            CanonicalAbiDirection.LoweredImport);
        var helperMethodName = _syntax.Format(new WitBindingSyntaxRequest.QualifiedFunctionName(interfaceName, function.Name));
        var methodName = _syntax.Format(new WitBindingSyntaxRequest.FunctionName(function.Name));
        var returnType = _syntax.Format(new WitBindingSyntaxRequest.ReturnType(document, function));
        writer.Line($"public static {returnType} {methodName}({_syntax.Format(new WitBindingSyntaxRequest.Parameters(document, function))})");
        writer.Line("{");
        writer.Indent();

        var lowerings = function.Parameters.Select((parameter, index) =>
            LowerParameter(document, parameter, index)).ToArray();
        foreach (var lowering in lowerings)
        {
            foreach (var declaration in lowering.Declarations)
            {
                writer.Line(declaration);
            }
        }
        var hasCleanup = lowerings.Any(lowering => lowering.Cleanup.Length != 0);
        if (function.Result is not null)
        {
            writer.Line($"{returnType} __managedResult = default!;");
        }
        if (hasCleanup)
        {
            writer.Line("try");
            writer.Line("{");
            writer.Indent();
        }
        foreach (var lowering in lowerings)
        {
            foreach (var statement in lowering.Setup)
            {
                writer.Line(statement);
            }
        }
        foreach (var lowering in lowerings.Where(lowering => lowering.TransferOwnership))
        {
            writer.Line(lowering.HighExpression + ".Invalidate();");
        }

        var flatArguments = lowerings.SelectMany(lowering => lowering.FlatExpressions)
            .ToArray();
        string callArguments;
        if (model.Signature.IndirectParameters)
        {
            var parameters = ParameterRecord(model.Function);
            writer.Line($"var __parameterBlock = CanonicalAbi.Allocate({Size(parameters)}, {Alignment(parameters)});");
            writer.Line("try");
            writer.Line("{");
            writer.Indent();
            WriteParameterBlock(writer, document, function, model.Function, lowerings,
                "__parameterBlock");
            callArguments = "__parameterBlock";
        }
        else
        {
            callArguments = string.Join(", ", flatArguments);
        }

        if (model.Signature.IndirectResult)
        {
            var resultType = model.Function.Result!;
            writer.Line($"var __canonicalResult = CanonicalAbi.Allocate({Size(resultType)}, {Alignment(resultType)});");
            writer.Line("var __canonicalResultInitialized = false;");
            writer.Line("try");
            writer.Line("{");
            writer.Indent();
            callArguments = callArguments.Length == 0
                ? "__canonicalResult"
                : callArguments + ", __canonicalResult";
        }

        var call = $"__Canonical{helperMethodName}({callArguments})";
        if (function.Result is null)
        {
            writer.Line(call + ";");
        }
        else if (model.Signature.IndirectResult)
        {
            writer.Line(call + ";");
            writer.Line("__canonicalResultInitialized = true;");
            WriteLiftImportResult(writer, document, function.Result,
                model.Signature, "__canonicalResult", "__managedResult", typeSet);
        }
        else
        {
            writer.Line("var __canonicalResult = " + call + ";");
            WriteLiftImportResult(writer, document, function.Result,
                model.Signature, "__canonicalResult", "__managedResult", typeSet);
        }
        if (model.Signature.IndirectResult)
        {
            writer.Unindent();
            writer.Line("}");
            writer.Line("finally");
            writer.Line("{");
            writer.Indent();
            writer.Line("if (__canonicalResultInitialized)");
            writer.Line("{");
            writer.Indent();
            WriteFreeResultMemory(writer, document, function.Result!, "__canonicalResult");
            writer.Unindent();
            writer.Line("}");
            writer.Line("else");
            writer.Line("{");
            writer.Indent();
            writer.Line("CanonicalAbi.Free(__canonicalResult);");
            writer.Unindent();
            writer.Line("}");
            writer.Unindent();
            writer.Line("}");
        }
        if (model.Signature.IndirectParameters)
        {
            writer.Unindent();
            writer.Line("}");
            writer.Line("finally");
            writer.Line("{");
            writer.Indent();
            writer.Line("CanonicalAbi.Free(__parameterBlock);");
            writer.Unindent();
            writer.Line("}");
        }
        if (hasCleanup)
        {
            writer.Unindent();
            writer.Line("}");
            writer.Line("finally");
            writer.Line("{");
            writer.Indent();
            foreach (var statement in lowerings
                         .SelectMany(lowering => lowering.Cleanup)
                         .Reverse())
            {
                writer.Line(statement);
            }
            writer.Unindent();
            writer.Line("}");
        }
        if (function.Result is not null)
        {
            writer.Line("return __managedResult;");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"[WitImport(\"{interfaceName}\", \"{function.Name}\")]");
        writer.Line($"private static extern {RawType(model.Signature.Result)} __Canonical{helperMethodName}({RawParameters(model.Signature)});");
    }

    protected void WriteExports(
        CodeWriter writer,
        WitDocument document,
        WitWorld world,
        FlatBindingTypeSet typeSet)
    {
        writer.Line($"public static partial class {_syntax.Format(new WitBindingSyntaxRequest.Identifier(world.Name))}Exports");
        writer.Line("{");
        writer.Indent();
        foreach (var item in world.Exports)
        {
            if (item.Function is not null)
            {
                WriteExport(writer, document, string.Empty, item.Function, typeSet);
                writer.Line();
                continue;
            }
            var @interface = document.Interfaces[item.InterfaceId!.Value];
            foreach (var function in @interface.Functions)
            {
                WriteExport(
                    writer,
                    document,
                    $"{@interface.Package}/{@interface.Name}",
                    function,
                    typeSet);
                writer.Line();
            }
        }
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteExport(
        CodeWriter writer,
        WitDocument document,
        string interfaceName,
        WitFunction function,
        FlatBindingTypeSet typeSet)
    {
        var model = Model(
            document,
            interfaceName,
            function,
            CanonicalAbiDirection.LiftedExport);
        var helperMethodName = _syntax.Format(new WitBindingSyntaxRequest.QualifiedFunctionName(interfaceName, function.Name));
        var methodName = _syntax.Format(new WitBindingSyntaxRequest.FunctionName(function.Name));
        writer.Line($"public static partial {_syntax.Format(new WitBindingSyntaxRequest.ReturnType(document, function))} {methodName}({_syntax.Format(new WitBindingSyntaxRequest.Parameters(document, function))});");
        writer.Line();
        writer.Line($"[WitExport(\"{interfaceName}\", \"{function.Name}\")]");
        writer.Line($"private static {RawType(model.Signature.Result)} __Canonical{helperMethodName}({RawParameters(model.Signature)})");
        writer.Line("{");
        writer.Indent();

        var lifted = LiftExportParameters(
            writer,
            document,
            function,
            model.Function,
            model.Signature,
            typeSet);
        var hasCleanup = lifted.Cleanup.Length != 0;
        if (hasCleanup)
        {
            writer.Line("try");
            writer.Line("{");
            writer.Indent();
        }
        var call = $"{methodName}({string.Join(", ", lifted.Arguments)})";
        if (function.Result is null)
        {
            writer.Line(call + ";");
        }
        else
        {
            writer.Line("var __managedResult = " + call + ";");
            WriteLowerExportResult(
                writer,
                document,
                function.Result,
                model.Signature,
                "__managedResult",
                typeSet);
        }
        if (hasCleanup)
        {
            writer.Unindent();
            writer.Line("}");
            writer.Line("finally");
            writer.Line("{");
            writer.Indent();
            foreach (var statement in lifted.Cleanup.Reverse())
            {
                writer.Line(statement);
            }
            writer.Unindent();
            writer.Line("}");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"[WitPostReturn(\"{interfaceName}\", \"{function.Name}\")]");
        writer.Line($"private static void __Canonical{helperMethodName}Post({PostReturnParameter(model.Signature)})");
        writer.Line("{");
        writer.Indent();
        WritePostReturn(writer, document, function.Result, model.Signature);
        writer.Unindent();
        writer.Line("}");
    }

    protected void WriteFlatBindingMethods(
        CodeWriter writer,
        WitDocument document,
        FlatBindingTypeSet typeSet)
    {
        if (typeSet.Lift.Count == 0 && typeSet.Lower.Count == 0)
        {
            return;
        }
        writer.Line();
        writer.Line("internal static class __CanonicalFlat");
        writer.Line("{");
        writer.Indent();
        foreach (var id in typeSet.Lift.Concat(typeSet.Lower).Distinct().Order())
        {
            var reference = new WitTypeReference.Defined(id);
            var type = _types.Resolve(document, reference);
            var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
            var flat = _flattener.Flatten(type);
            if (typeSet.Lower.Contains(id))
            {
                writer.Line($"internal static {RawType(flat.Single())} LowerType{id}({typeName} value)");
                writer.Line("{");
                writer.Indent();
                writer.Line($"var buffer = __CanonicalMarshalling.LowerType{id}(value, true);");
                writer.Line("try");
                writer.Line("{");
                writer.Indent();
                writer.Line("return " + ReadFlat(document, reference,
                    "buffer.Address", "0").Single() + ";");
                writer.Unindent();
                writer.Line("}");
                writer.Line("finally");
                writer.Line("{");
                writer.Indent();
                writer.Line($"__CanonicalMarshalling.FreeType{id}(buffer.Address);");
                writer.Unindent();
                writer.Line("}");
                writer.Unindent();
                writer.Line("}");
                writer.Line();
            }
            if (typeSet.Lift.Contains(id))
            {
                var parameters = string.Join(", ", flat
                    .Select((kind, index) => $"{RawType(kind)} value{index}")
                    .Append("bool exportBoundary"));
                writer.Line($"internal static {typeName} LiftType{id}({parameters})");
                writer.Line("{");
                writer.Indent();
                writer.Line($"var address = CanonicalAbi.Allocate({Size(type)}, {Alignment(type)});");
                writer.Line("try");
                writer.Line("{");
                writer.Indent();
                foreach (var statement in WriteFlat(
                             document,
                             reference,
                             "address",
                             "0",
                             [.. Enumerable.Range(0, flat.Length)
                                 .Select(index => $"value{index}")]))
                {
                    writer.Line(statement);
                }
                writer.Line($"return __CanonicalMarshalling.LiftType{id}(address, exportBoundary);");
                writer.Unindent();
                writer.Line("}");
                writer.Line("finally");
                writer.Line("{");
                writer.Indent();
                writer.Line("CanonicalAbi.Free(address);");
                writer.Unindent();
                writer.Line("}");
                writer.Unindent();
                writer.Line("}");
                writer.Line();
            }
        }
        writer.Unindent();
        writer.Line("}");
    }

    private ParameterLowering LowerParameter(
        WitDocument document,
        WitParameter parameter,
        int index)
    {
        var high = _syntax.Format(new WitBindingSyntaxRequest.Variable(parameter.Name));
        var type = _types.Resolve(document, parameter.Type);
        var prefix = $"__p{index}";
        if (parameter.Type is WitTypeReference.Primitive primitive)
        {
            if (primitive.Name == "string")
            {
                return new(
                    [.. new[] { $"CanonicalBuffer {prefix} = default;" }],
                    [$"{prefix} = CanonicalAbi.LowerString({high});"],
                    [$"{prefix}.Address", $"{prefix}.Length"],
                    [$"{prefix}.Dispose();"],
                    false,
                    high);
            }
            return new([], [], [LowerPrimitive(primitive.Name, high)], [], false, high);
        }

        var defined = (WitTypeReference.Defined)parameter.Type;
        if (type.Kind is CanonicalAbiTypeKind.OwnedResource or
            CanonicalAbiTypeKind.BorrowedResource)
        {
            var transfersOwnership = type.Kind == CanonicalAbiTypeKind.OwnedResource;
            return new(
                [],
                transfersOwnership
                    ? [$"var {prefix}Handle = unchecked((int){high}.RawHandle);"]
                    : [],
                [transfersOwnership
                    ? $"{prefix}Handle"
                    : $"unchecked((int){high}.RawHandle)"],
                [],
                transfersOwnership,
                high);
        }
        var list = type.Kind == CanonicalAbiTypeKind.List;
        return new(
            [$"CanonicalBuffer {prefix} = default;"],
            [$"{prefix} = __CanonicalMarshalling.LowerType{defined.Id}({high}, false);"],
            list
                ? [$"{prefix}.Address", $"{prefix}.Length"]
                : [.. ReadFlat(document, parameter.Type, $"{prefix}.Address", "0")],
            list
                ? [$"if ({prefix}.Address != 0) __CanonicalMarshalling.FreeType{defined.Id}({prefix}.Address, {prefix}.Length);"]
                : [$"if ({prefix}.Address != 0) __CanonicalMarshalling.FreeType{defined.Id}({prefix}.Address);"],
            false,
            high);
    }

    private void WriteLiftImportResult(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference result,
        CanonicalAbiCoreSignature signature,
        string canonical,
        string destination,
        FlatBindingTypeSet typeSet)
    {
        if (!signature.IndirectResult)
        {
            writer.Line($"{destination} = {LiftDirect(document, result, [canonical], typeSet)};");
            return;
        }
        writer.Line($"{destination} = {LiftFromMemory(document, result, canonical)};");
    }

    private LiftedParameters LiftExportParameters(
        CodeWriter writer,
        WitDocument document,
        WitFunction witFunction,
        CanonicalAbiFunction function,
        CanonicalAbiCoreSignature signature,
        FlatBindingTypeSet typeSet)
    {
        var arguments = ImmutableArray.CreateBuilder<string>();
        var cleanup = ImmutableArray.CreateBuilder<string>();
        var flatIndex = 0;
        CanonicalAbiMemoryLayout? parameterLayout32 = null;
        CanonicalAbiMemoryLayout? parameterLayout64 = null;
        if (signature.IndirectParameters)
        {
            var parameterRecord = ParameterRecord(function);
            parameterLayout32 = _layouts.Plan(parameterRecord, WasmTarget.Wasm32);
            parameterLayout64 = _layouts.Plan(parameterRecord, WasmTarget.Wasm64);
        }
        for (var index = 0; index < function.Parameters.Length; index++)
        {
            var parameter = function.Parameters[index];
            var sourceReference = witFunction.Parameters[index].Type;
            string expression;
            if (signature.IndirectParameters)
            {
                var offset = Width(
                    parameterLayout32!.Fields[index].Offset,
                    parameterLayout64!.Fields[index].Offset);
                expression = LiftFromMemory(
                    document,
                    sourceReference,
                    Add("__parameters", offset),
                    exportResource: true);
            }
            else
            {
                var count = _flattener.Flatten(parameter.Type).Length;
                var values = Enumerable.Range(flatIndex, count)
                    .Select(slot => $"__f{slot}")
                    .ToArray();
                expression = LiftDirect(
                    document,
                    sourceReference,
                    values,
                    typeSet,
                    exportResource: true);
                flatIndex += count;
            }
            var name = $"__managed{index}";
            writer.Line($"var {name} = {expression};");
            arguments.Add(name);
            if (parameter.Type.Kind == CanonicalAbiTypeKind.BorrowedResource)
            {
                cleanup.Add(name + ".Invalidate();");
            }
        }
        return new(arguments.ToImmutable(), cleanup.ToImmutable());
    }

    private void WriteLowerExportResult(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference result,
        CanonicalAbiCoreSignature signature,
        string value,
        FlatBindingTypeSet typeSet)
    {
        if (!signature.IndirectResult)
        {
            var lowering = LowerResultDirect(document, result, value, typeSet);
            writer.Line("return " + lowering.Expression + ";");
            return;
        }

        var type = _types.Resolve(document, result);
        if (result is WitTypeReference.Primitive { Name: "string" })
        {
            writer.Line("var __value = CanonicalAbi.LowerString(" + value + ");");
            writer.Line("var __result = CanonicalAbi.Allocate((nuint)(UIntPtr.Size * 2), (nuint)UIntPtr.Size);");
            writer.Line("CanonicalAbi.WriteAddress(__result, 0, __value.Address);");
            writer.Line("CanonicalAbi.WriteAddress(__result, (nuint)UIntPtr.Size, __value.Length);");
            writer.Line("return __result;");
            return;
        }
        var defined = (WitTypeReference.Defined)result;
        if (type.Kind == CanonicalAbiTypeKind.List)
        {
            writer.Line($"var __value = __CanonicalMarshalling.LowerType{defined.Id}({value}, true);");
            writer.Line("var __result = CanonicalAbi.Allocate((nuint)(UIntPtr.Size * 2), (nuint)UIntPtr.Size);");
            writer.Line("CanonicalAbi.WriteAddress(__result, 0, __value.Address);");
            writer.Line("CanonicalAbi.WriteAddress(__result, (nuint)UIntPtr.Size, __value.Length);");
            writer.Line("return __result;");
            return;
        }
        writer.Line($"return __CanonicalMarshalling.LowerType{defined.Id}({value}, true).Address;");
    }

    private void WritePostReturn(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference? result,
        CanonicalAbiCoreSignature signature)
    {
        if (result is null || !signature.IndirectResult)
        {
            return;
        }
        WriteFreeResultMemory(writer, document, result, "__result");
    }

    private void WriteFreeResultMemory(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference result,
        string address)
    {
        var type = _types.Resolve(document, result);
        if (result is WitTypeReference.Primitive { Name: "string" })
        {
            writer.Line($"CanonicalAbi.Free(CanonicalAbi.ReadAddress({address}, 0));");
            writer.Line($"CanonicalAbi.Free({address});");
            return;
        }
        var defined = (WitTypeReference.Defined)result;
        if (type.Kind == CanonicalAbiTypeKind.List)
        {
            writer.Line($"__CanonicalMarshalling.FreeType{defined.Id}(CanonicalAbi.ReadAddress({address}, 0), CanonicalAbi.ReadAddress({address}, (nuint)UIntPtr.Size));");
            writer.Line($"CanonicalAbi.Free({address});");
            return;
        }
        writer.Line($"__CanonicalMarshalling.FreeType{defined.Id}({address});");
    }

    private DirectLowering LowerResultDirect(
        WitDocument document,
        WitTypeReference result,
        string value,
        FlatBindingTypeSet typeSet)
    {
        if (result is WitTypeReference.Primitive primitive)
        {
            return new(LowerPrimitive(primitive.Name, value));
        }
        var type = _types.Resolve(document, result);
        if (type.Kind is CanonicalAbiTypeKind.OwnedResource or
            CanonicalAbiTypeKind.BorrowedResource)
        {
            return new($"unchecked((int)({value}).LowerExport())");
        }
        var defined = (WitTypeReference.Defined)result;
        typeSet.Lower.Add(defined.Id);
        return new($"__CanonicalFlat.LowerType{defined.Id}({value})");
    }

    private string LiftDirect(
        WitDocument document,
        WitTypeReference reference,
        string[] values,
        FlatBindingTypeSet typeSet,
        bool exportResource = false)
    {
        if (reference is WitTypeReference.Primitive primitive)
        {
            if (primitive.Name == "string")
            {
                return $"CanonicalAbi.LiftString({values[0]}, {values[1]})";
            }
            return LiftPrimitive(primitive.Name, values[0]);
        }
        var type = _types.Resolve(document, reference);
        var defined = (WitTypeReference.Defined)reference;
        if (type.Kind is CanonicalAbiTypeKind.OwnedResource or
            CanonicalAbiTypeKind.BorrowedResource)
        {
            if (exportResource)
            {
                return $"{_syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference))}.LiftExport(unchecked((uint){values[0]}), {(type.Kind == CanonicalAbiTypeKind.OwnedResource ? "true" : "false")})";
            }
            return $"new {_syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference))}(unchecked((uint){values[0]}), {(type.Kind == CanonicalAbiTypeKind.OwnedResource ? "true" : "false")})";
        }
        if (type.Kind == CanonicalAbiTypeKind.List)
        {
            // A direct list is only lifted for an exported parameter; imported
            // list results use the indirect memory path below.
            return $"__CanonicalMarshalling.LiftType{defined.Id}({values[0]}, {values[1]}, true)";
        }

        typeSet.Lift.Add(defined.Id);
        return $"__CanonicalFlat.LiftType{defined.Id}({string.Join(", ", values.Append(exportResource ? "true" : "false"))})";
    }

    private string LiftFromMemory(
        WitDocument document,
        WitTypeReference reference,
        string address,
        bool exportResource = false)
    {
        if (reference is WitTypeReference.Primitive primitive)
        {
            return LiftPrimitiveFromMemory(primitive.Name, address, "0");
        }
        var type = _types.Resolve(document, reference);
        var defined = (WitTypeReference.Defined)reference;
        if (type.Kind is CanonicalAbiTypeKind.OwnedResource or
            CanonicalAbiTypeKind.BorrowedResource)
        {
            var handle = $"unchecked((uint)CanonicalAbi.ReadInt32({address}, 0))";
            // An indirect resource is only a parameter on an exported
            // function; resource results have one flat slot and cannot be
            // indirect.
            return $"{_syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference))}.LiftExport({handle}, {(type.Kind == CanonicalAbiTypeKind.OwnedResource ? "true" : "false")})";
        }
        if (type.Kind == CanonicalAbiTypeKind.List)
        {
            return $"__CanonicalMarshalling.LiftType{defined.Id}(CanonicalAbi.ReadAddress({address}, 0), CanonicalAbi.ReadAddress({address}, (nuint)UIntPtr.Size), {(exportResource ? "true" : "false")})";
        }
        return $"__CanonicalMarshalling.LiftType{defined.Id}({address}, {(exportResource ? "true" : "false")})";
    }

    private void WriteParameterBlock(
        CodeWriter writer,
        WitDocument document,
        WitFunction witFunction,
        CanonicalAbiFunction function,
        IReadOnlyList<ParameterLowering> lowerings,
        string address)
    {
        var record = ParameterRecord(function);
        var layout32 = _layouts.Plan(record, WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(record, WasmTarget.Wasm64);
        for (var index = 0; index < function.Parameters.Length; index++)
        {
            var offset = Width(
                layout32.Fields[index].Offset,
                layout64.Fields[index].Offset);
            foreach (var statement in WriteFlat(
                         document,
                         witFunction.Parameters[index].Type,
                         address,
                         offset,
                         lowerings[index].FlatExpressions))
            {
                writer.Line(statement);
            }
        }
    }

    private ImmutableArray<string> ReadFlat(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var type = _types.Resolve(document, reference);
        var readers = new Dictionary<CanonicalAbiTypeKind, Func<ImmutableArray<string>>>
        {
            [CanonicalAbiTypeKind.Bool] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.S8] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.U8] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.S16] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.U16] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.S32] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.U32] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.Character] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.Enum] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.OwnedResource] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.BorrowedResource] = () => [ReadI32(document, reference, address, offset)],
            [CanonicalAbiTypeKind.S64] = () => [$"CanonicalAbi.ReadInt64({address}, {offset})"],
            [CanonicalAbiTypeKind.U64] = () => [$"CanonicalAbi.ReadInt64({address}, {offset})"],
            [CanonicalAbiTypeKind.F32] = () => [$"CanonicalAbi.ReadSingle({address}, {offset})"],
            [CanonicalAbiTypeKind.F64] = () => [$"CanonicalAbi.ReadDouble({address}, {offset})"],
            [CanonicalAbiTypeKind.Text] = () =>
                [$"CanonicalAbi.ReadAddress({address}, {offset})",
                    $"CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")})"],
            [CanonicalAbiTypeKind.List] = () =>
                [$"CanonicalAbi.ReadAddress({address}, {offset})",
                    $"CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")})"],
            [CanonicalAbiTypeKind.Alias] = () =>
                ReadFlat(document, AliasReference(document, reference), address, offset),
            [CanonicalAbiTypeKind.Record] = () =>
                ReadAggregate(document, reference, address, offset),
            [CanonicalAbiTypeKind.Tuple] = () =>
                ReadAggregate(document, reference, address, offset),
            [CanonicalAbiTypeKind.Flags] = () => ReadFlags(type, address, offset),
            [CanonicalAbiTypeKind.Option] = () =>
                ReadVariant(document, reference, address, offset),
            [CanonicalAbiTypeKind.Result] = () =>
                ReadVariant(document, reference, address, offset),
            [CanonicalAbiTypeKind.Variant] = () =>
                ReadVariant(document, reference, address, offset),
        };
        return readers[type.Kind]();
    }

    private ImmutableArray<string> WriteFlat(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        IReadOnlyList<string> values)
    {
        var type = _types.Resolve(document, reference);
        if (type.Kind == CanonicalAbiTypeKind.Alias)
        {
            return WriteFlat(
                document,
                AliasReference(document, reference),
                address,
                offset,
                values);
        }
        if (type.Kind is CanonicalAbiTypeKind.Option or
            CanonicalAbiTypeKind.Result or CanonicalAbiTypeKind.Variant)
        {
            return WriteVariantFlat(document, reference, address, offset, values);
        }
        if (type.Kind is CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple)
        {
            return WriteAggregateFlat(document, reference, address, offset, values);
        }
        var slots = ReadFlat(document, reference, address, offset);
        var statements = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < slots.Length; index++)
        {
            statements.Add(WriteSlot(slots[index], values[index]));
        }
        return statements.ToImmutable();
    }

    private ImmutableArray<string> WriteAggregateFlat(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        IReadOnlyList<string> values)
    {
        var type = _types.Resolve(document, reference);
        var layout32 = _layouts.Plan(type, WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(type, WasmTarget.Wasm64);
        var definition = document.Types[((WitTypeReference.Defined)reference).Id];
        var kind = definition.Kind.EnumerateObject().Single();
        var references = kind.Name == "record"
            ? kind.Value.GetProperty("fields").EnumerateArray()
                .Select(field => Reference(field.GetProperty("type"))).ToArray()
            : kind.Value.GetProperty("types").EnumerateArray()
                .Select(Reference).ToArray();
        var statements = ImmutableArray.CreateBuilder<string>();
        var flatIndex = 0;
        for (var index = 0; index < references.Length; index++)
        {
            var field = references[index];
            var fieldWidth = _flattener.Flatten(_types.Resolve(document, field)).Length;
            statements.AddRange(WriteFlat(
                document,
                field,
                address,
                Add(offset, Width(
                    layout32.Fields[index].Offset,
                    layout64.Fields[index].Offset)),
                values.Skip(flatIndex).Take(fieldWidth).ToArray()));
            flatIndex += fieldWidth;
        }
        return statements.ToImmutable();
    }

    private ImmutableArray<string> ReadAggregate(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var type = _types.Resolve(document, reference);
        var layout32 = _layouts.Plan(type, WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(type, WasmTarget.Wasm64);
        var definition = document.Types[((WitTypeReference.Defined)reference).Id];
        var kind = definition.Kind.EnumerateObject().Single();
        var references = kind.Name == "record"
            ? kind.Value.GetProperty("fields").EnumerateArray()
                .Select(field => Reference(field.GetProperty("type"))).ToArray()
            : kind.Value.GetProperty("types").EnumerateArray()
                .Select(Reference).ToArray();
        return [.. references.SelectMany((field, index) => ReadFlat(
            document,
            field,
            address,
            Add(offset, Width(layout32.Fields[index].Offset,
                layout64.Fields[index].Offset))))];
    }

    private ImmutableArray<string> ReadVariant(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var type = _types.Resolve(document, reference);
        var cases = VariantCases(type);
        var joined = _flattener.Flatten(type);
        var tag = ReadDiscriminant(type, address, offset);
        var result = ImmutableArray.CreateBuilder<string>();
        result.Add(tag);
        var payloadOffset = Add(offset, PayloadOffset(type));
        var references = VariantCaseReferences(document, reference);
        var caseValues = references.Select(caseReference => caseReference is null
                ? ImmutableArray<string>.Empty
                : ReadFlat(document, caseReference,
                    address, payloadOffset))
            .ToArray();
        var caseKinds = cases.Select(@case => @case.Type is null
                ? ImmutableArray<CliValueKind>.Empty
                : _flattener.Flatten(@case.Type))
            .ToArray();
        for (var slot = 1; slot < joined.Length; slot++)
        {
            var arms = Enumerable.Range(0, cases.Length).Select(index =>
            {
                var value = slot - 1 < caseValues[index].Length
                    ? _slotCoercions.Format(new WitFlatSlotCoercionRequest(
                        caseValues[index][slot - 1],
                        caseKinds[index][slot - 1],
                        joined[slot]))
                    : _slotCoercions.Format(new WitFlatSlotCoercionRequest(
                        Zero(joined[slot]),
                        joined[slot],
                        joined[slot]));
                return $"{index} => {value}";
            });
            result.Add($"{tag} switch {{ {string.Join(", ", arms)}, _ => throw new ArgumentException() }}");
        }
        return result.ToImmutable();
    }

    private ImmutableArray<string> WriteVariantFlat(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        IReadOnlyList<string> values)
    {
        var type = _types.Resolve(document, reference);
        var cases = VariantCases(type);
        var joined = _flattener.Flatten(type);
        var statements = ImmutableArray.CreateBuilder<string>();
        statements.Add(WriteDiscriminant(type, address, offset, values[0]));
        statements.Add($"switch (CanonicalAbi.LiftDiscriminant({values[0]}, {cases.Length})) {{");
        var payloadOffset = Add(offset, PayloadOffset(type));
        var references = VariantCaseReferences(document, reference);
        for (var index = 0; index < cases.Length; index++)
        {
            statements.Add($"case {index}:");
            if (references[index] is not null)
            {
                var referenceForCase = references[index]!;
                var kinds = _flattener.Flatten(cases[index].Type!);
                var converted = kinds.Select((kind, slot) =>
                    _slotCoercions.Format(new WitFlatSlotCoercionRequest(
                        values[slot + 1],
                        joined[slot + 1],
                        kind))).ToArray();
                statements.AddRange(WriteFlat(document, referenceForCase,
                    address, payloadOffset, converted));
            }
            statements.Add("break;");
        }
        statements.Add("}");
        return statements.ToImmutable();
    }

#pragma warning disable CS8509 // reader expressions originate from ReadFlat's closed set
    private static readonly Dictionary<
        string,
        Func<string, (string Method, string Converted)>> WritableReaders =
        new Dictionary<string, Func<string, (string Method, string Converted)>>(StringComparer.Ordinal)
        {
            ["ReadByte"] = value => ("WriteByte", $"checked((byte)({value}))"),
            ["ReadUInt16"] = value => ("WriteUInt16", $"checked((ushort)({value}))"),
            ["ReadInt32"] = value => ("WriteInt32", value),
            ["ReadInt64"] = value => ("WriteInt64", value),
            ["ReadSingle"] = value => ("WriteSingle", value),
            ["ReadDouble"] = value => ("WriteDouble", value),
            ["ReadAddress"] = value => ("WriteAddress", value),
        };

    private static string WriteSlot(
        string readExpression,
        string value)
    {
        const string prefix = "CanonicalAbi.Read";
        var methodStart = readExpression.IndexOf(prefix, StringComparison.Ordinal);
        var argumentsStart = readExpression.IndexOf('(', methodStart);
        var readMethod = readExpression[(methodStart + "CanonicalAbi.".Length)..argumentsStart];
        var (method, converted) = WritableReaders[readMethod](value);
        var arguments = readExpression[(argumentsStart + 1)..^1];
        return $"CanonicalAbi.{method}({arguments}, {converted});";
    }

#pragma warning restore CS8509

#pragma warning disable CS8509 // primitive names are validated by WitCanonicalTypeResolver
    private static readonly Dictionary<string, Func<string, string>> LowerPrimitiveOperations =
        new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
        {
            ["bool"] = value => $"{value} ? 1 : 0",
            ["s8"] = value => $"(int)({value})",
            ["s16"] = value => $"(int)({value})",
            ["s32"] = value => $"(int)({value})",
            ["u8"] = value => $"unchecked((int)({value}))",
            ["u16"] = value => $"unchecked((int)({value}))",
            ["u32"] = value => $"unchecked((int)({value}))",
            ["s64"] = value => value,
            ["u64"] = value => $"unchecked((long)({value}))",
            ["f32"] = value => value,
            ["f64"] = value => value,
            ["char"] = value => $"unchecked((int)CanonicalAbi.LiftCharacter(unchecked((int)({value}))))",
        };

    private static string LowerPrimitive(string name, string value) =>
        LowerPrimitiveOperations[name](value);

    private static readonly Dictionary<string, Func<string, string>> LiftPrimitiveOperations =
        new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
        {
            ["bool"] = value => $"CanonicalAbi.LiftBoolean({value})",
            ["s8"] = value => $"unchecked((sbyte)({value}))",
            ["u8"] = value => $"unchecked((byte)({value}))",
            ["s16"] = value => $"unchecked((short)({value}))",
            ["u16"] = value => $"unchecked((ushort)({value}))",
            ["s32"] = value => value,
            ["u32"] = value => $"unchecked((uint)({value}))",
            ["s64"] = value => value,
            ["u64"] = value => $"unchecked((ulong)({value}))",
            ["f32"] = value => value,
            ["f64"] = value => value,
            ["char"] = value => $"CanonicalAbi.LiftCharacter({value})",
        };

    private static string LiftPrimitive(string name, string value) =>
        LiftPrimitiveOperations[name](value);

    private static readonly Dictionary<string, Func<string, string, string>> LiftPrimitiveFromMemoryOperations =
        new Dictionary<string, Func<string, string, string>>(StringComparer.Ordinal)
        {
            ["bool"] = (address, offset) => $"CanonicalAbi.LiftBoolean(CanonicalAbi.ReadByte({address}, {offset}))",
            ["s8"] = (address, offset) => $"unchecked((sbyte)CanonicalAbi.ReadByte({address}, {offset}))",
            ["u8"] = (address, offset) => $"CanonicalAbi.ReadByte({address}, {offset})",
            ["s16"] = (address, offset) => $"unchecked((short)CanonicalAbi.ReadUInt16({address}, {offset}))",
            ["u16"] = (address, offset) => $"CanonicalAbi.ReadUInt16({address}, {offset})",
            ["s32"] = (address, offset) => $"CanonicalAbi.ReadInt32({address}, {offset})",
            ["u32"] = (address, offset) => $"unchecked((uint)CanonicalAbi.ReadInt32({address}, {offset}))",
            ["s64"] = (address, offset) => $"CanonicalAbi.ReadInt64({address}, {offset})",
            ["u64"] = (address, offset) => $"unchecked((ulong)CanonicalAbi.ReadInt64({address}, {offset}))",
            ["f32"] = (address, offset) => $"CanonicalAbi.ReadSingle({address}, {offset})",
            ["f64"] = (address, offset) => $"CanonicalAbi.ReadDouble({address}, {offset})",
            ["char"] = (address, offset) => $"CanonicalAbi.LiftCharacter(CanonicalAbi.ReadInt32({address}, {offset}))",
            ["string"] = (address, offset) => $"CanonicalAbi.LiftString(CanonicalAbi.ReadAddress({address}, {offset}), CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}))",
        };

    private static string LiftPrimitiveFromMemory(
        string name,
        string address,
        string offset) => LiftPrimitiveFromMemoryOperations[name](address, offset);

    private string ReadI32(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var type = _types.Resolve(document, reference);
        if (type.Kind == CanonicalAbiTypeKind.Enum)
        {
            return ReadDiscriminant(type, address, offset);
        }
        return $"CanonicalAbi.ReadInt32({address}, {offset})";
    }
#pragma warning restore CS8509

    private static readonly Dictionary<int, Func<string, string, int, string>> FlagReaders =
        new Dictionary<int, Func<string, string, int, string>>
        {
            [1] = (address, offset, _) =>
                $"CanonicalAbi.ReadByte({address}, {offset})",
            [2] = (address, offset, _) =>
                $"CanonicalAbi.ReadUInt16({address}, {offset})",
            [3] = (address, offset, index) =>
                $"CanonicalAbi.ReadInt32({address}, {Add(offset, $"(nuint){index * 4}")})",
        };

    private static ImmutableArray<string> ReadFlags(
        CanonicalAbiType type,
        string address,
        string offset)
    {
        var width = Math.Clamp((type.FlagsCount + 7) / 8, 1, 3);
        var reader = FlagReaders[width];
        return [.. Enumerable.Range(
            0,
            Math.Max(1, (type.FlagsCount + 31) / 32))
            .Select(index => reader(address, offset, index))];
    }

    private static ImmutableArray<CanonicalAbiCase> VariantCases(
        CanonicalAbiType type)
    {
        if (type.Kind == CanonicalAbiTypeKind.Option)
        {
            return [new("none", null), new("some", type.ElementType)];
        }
        if (type.Kind == CanonicalAbiTypeKind.Result)
        {
            return [new("ok", type.SuccessType), new("error", type.ErrorType)];
        }
        return type.Cases;
    }

    private static ImmutableArray<WitTypeReference?> VariantCaseReferences(
        WitDocument document,
        WitTypeReference reference)
    {
        var definition = document.Types[((WitTypeReference.Defined)reference).Id];
        var kind = definition.Kind.EnumerateObject().Single();
        if (kind.Name == "option")
        {
            return [null, Reference(kind.Value)];
        }
        if (kind.Name == "result")
        {
            return
            [
                NullableReference(kind.Value.GetProperty("ok")),
                NullableReference(kind.Value.GetProperty("err")),
            ];
        }
        return [.. kind.Value.GetProperty("cases").EnumerateArray()
            .Select(item => NullableReference(item.GetProperty("type")))];
    }

    private static WitTypeReference? NullableReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : Reference(value);

    private string ReadDiscriminant(
        CanonicalAbiType type,
        string address,
        string offset)
    {
        var size = _layouts.Plan(type, WasmTarget.Wasm32).DiscriminantSize;
        if (size == 0)
        {
            size = _layouts.Plan(type, WasmTarget.Wasm32).Size;
        }
        return size switch
        {
            1 => $"CanonicalAbi.ReadByte({address}, {offset})",
            2 => $"CanonicalAbi.ReadUInt16({address}, {offset})",
            _ => $"CanonicalAbi.ReadInt32({address}, {offset})",
        };
    }

    private string WriteDiscriminant(
        CanonicalAbiType type,
        string address,
        string offset,
        string value)
    {
        var size = _layouts.Plan(type, WasmTarget.Wasm32).DiscriminantSize;
        return size switch
        {
            1 => $"CanonicalAbi.WriteByte({address}, {offset}, checked((byte)({value})));",
            2 => $"CanonicalAbi.WriteUInt16({address}, {offset}, checked((ushort)({value})));",
            _ => $"CanonicalAbi.WriteInt32({address}, {offset}, ({value}));",
        };
    }

    private string PayloadOffset(CanonicalAbiType type) => Width(
        _layouts.Plan(type, WasmTarget.Wasm32).PayloadOffset,
        _layouts.Plan(type, WasmTarget.Wasm64).PayloadOffset);

    private string Size(CanonicalAbiType type) => Width(
        _layouts.Plan(type, WasmTarget.Wasm32).Size,
        _layouts.Plan(type, WasmTarget.Wasm64).Size);

    private string Alignment(CanonicalAbiType type) => Width(
        _layouts.Plan(type, WasmTarget.Wasm32).Alignment,
        _layouts.Plan(type, WasmTarget.Wasm64).Alignment);

    private static string Width(int wasm32, int wasm64) => wasm32 == wasm64
        ? $"(nuint){wasm32}"
        : $"(nuint)(UIntPtr.Size == 8 ? {wasm64} : {wasm32})";

    private static string Add(string left, string right) =>
        right is "0" or "(nuint)0" ? left : $"({left} + {right})";

    private static string Zero(CliValueKind kind) => kind switch
    {
        CliValueKind.F4 => "0f",
        CliValueKind.F8 => "0d",
        CliValueKind.I8 => "0L",
        _ => "0",
    };

    private static readonly Dictionary<CliValueKind, string> RawTypes =
        new Dictionary<CliValueKind, string>
        {
            [CliValueKind.Void] = "void",
            [CliValueKind.I4] = "int",
            [CliValueKind.I8] = "long",
            [CliValueKind.F4] = "float",
            [CliValueKind.F8] = "double",
            [CliValueKind.ManagedAddress] = "nuint",
        };

    private static string RawType(CliValueKind kind) => RawTypes[kind];

    private static string RawParameters(CanonicalAbiCoreSignature signature) =>
        string.Join(", ", signature.Parameters.Select((kind, index) =>
            $"{RawType(kind)} {RawParameterName(signature, index)}"));

    private static string RawParameterName(
        CanonicalAbiCoreSignature signature,
        int index)
    {
        if (signature.IndirectResult &&
            signature.Result == CliValueKind.Void &&
            index == signature.Parameters.Length - 1)
        {
            return "__result";
        }
        return signature.IndirectParameters ? "__parameters" : $"__f{index}";
    }

    private static string PostReturnParameter(
        CanonicalAbiCoreSignature signature) => signature.IndirectResult
            ? "nuint __result"
            : signature.Result == CliValueKind.Void
                ? string.Empty
                : $"{RawType(signature.Result)} __result";

    private WitBindingFunctionModel Model(
        WitDocument document,
        string interfaceName,
        WitFunction function,
        CanonicalAbiDirection direction)
        => _models.Build(document, interfaceName, function, direction);

    private static CanonicalAbiType ParameterRecord(CanonicalAbiFunction function) =>
        new(CanonicalAbiTypeKind.Record,
            CliTypeIdentity.FromStackKind(CliValueKind.Unknown))
        {
            Fields = [.. function.Parameters.Select(parameter =>
                new CanonicalAbiField(parameter.Name, parameter.Type))],
        };

    private static WitTypeReference AliasReference(
        WitDocument document,
        WitTypeReference reference)
    {
        var definition = document.Types[((WitTypeReference.Defined)reference).Id];
        return Reference(definition.Kind.EnumerateObject().Single().Value);
    }

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());

    private sealed record ParameterLowering(
        ImmutableArray<string> Declarations,
        ImmutableArray<string> Setup,
        ImmutableArray<string> FlatExpressions,
        ImmutableArray<string> Cleanup,
        bool TransferOwnership,
        string HighExpression);

    private sealed record LiftedParameters(
        ImmutableArray<string> Arguments,
        ImmutableArray<string> Cleanup);

    private sealed record DirectLowering(string Expression);

    protected sealed record FlatBindingTypeSet(
        HashSet<int> Lift,
        HashSet<int> Lower)
    {
        public FlatBindingTypeSet() : this([], [])
        {
        }
    }
}
