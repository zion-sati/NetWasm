using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

internal enum UnitQueryStatus
{
    Answered,
    // The query's argument cannot be rebuilt from its key.
    Unbuildable,
    // The provider now fails for it.
    Failed,
}

/// <summary>This compile's answer to a recorded query, fingerprinted as emission recorded it.</summary>
internal readonly record struct UnitQueryAnswer(UnitQueryStatus Status, UnitFingerprint Fingerprint);

internal interface IUnitQueryReplayer
{
    // Asks a recorded query again, as the unit's emission asked it, in this compile.
    UnitQueryAnswer Replay(
        UnitQueryKind kind,
        string key,
        UnitQueryArguments arguments,
        InstructionModuleTarget target);
}

/// <summary>
/// Re-asks recorded queries through the same recording providers and target view that
/// recorded them, so each answer is fingerprinted exactly as it was. A query's argument is
/// rebuilt from its key: text keys directly, types, fields and methods from the identities
/// the given arguments name. One per worker: it asks the worker's own providers.
/// </summary>
internal sealed class UnitQueryReplayer(
    IUnitDependencyRecorder recorder,
    Func<Action, ImmutableArray<UnitDependency>> recording,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields,
    IStaticFieldLayoutProvider statics,
    IStaticDataLayout data,
    IMemberDescriptorLayout descriptors,
    IManagedExceptionObjectProvider exceptions,
    ITypeDescriptorSource typeDescriptors,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fieldDefinitions) : IUnitQueryReplayer
{
    private const int FieldTable = 0x04;
    private InstructionModuleTarget? _target;
    private InstructionModuleTarget? _view;

    public UnitQueryAnswer Replay(
        UnitQueryKind kind,
        string key,
        UnitQueryArguments arguments,
        InstructionModuleTarget target)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(_target, target))
        {
            _view = UnitTargetViews.Recording(target, recorder);
            _target = target;
        }
        var asked = false;
        ImmutableArray<UnitDependency> answers;
        try
        {
            answers = recording(() => asked = Ask(kind, key, arguments, _view!));
        }
        catch (CompilerException)
        {
            return new(UnitQueryStatus.Failed, default);
        }
        catch (KeyNotFoundException)
        {
            return new(UnitQueryStatus.Failed, default);
        }
        if (asked)
        {
            foreach (var answer in answers)
            {
                if (answer.Kind == kind && string.Equals(answer.Key, key, StringComparison.Ordinal))
                    return new(UnitQueryStatus.Answered, answer.Answer);
            }
        }
        return new(UnitQueryStatus.Unbuildable, default);
    }

    private bool Ask(UnitQueryKind kind, string key, UnitQueryArguments arguments, InstructionModuleTarget view)
    {
        switch (kind)
        {
            case UnitQueryKind.ObjectLayout:
                if (!TryType(key, arguments, out var type)) return false;
                _ = types.GetObjectLayout(type);
                return true;
            case UnitQueryKind.ObjectLayoutLookup:
                if (!TryType(key, arguments, out type)) return false;
                _ = types.GetObjectLayout(type, out _);
                return true;
            case UnitQueryKind.ObjectLayoutOfDefinition:
                if (!TryParse(key, out var definition)) return false;
                _ = types.GetObjectLayout(definition);
                return true;
            case UnitQueryKind.ReferenceArrayTypeId:
                _ = types.ReferenceArrayTypeId;
                return true;
            case UnitQueryKind.StringTypeId:
                _ = types.StringTypeId;
                return true;
            case UnitQueryKind.TypeTypeId:
                _ = types.TypeTypeId;
                return true;
            case UnitQueryKind.ValueLayout:
                if (!TryType(key, arguments, out type)) return false;
                _ = values.GetValueLayout(type);
                return true;
            case UnitQueryKind.InstanceField:
                if (!TryField(key, arguments, out var field)) return false;
                _ = fields.GetFieldLayout(field);
                return true;
            case UnitQueryKind.InstanceFieldOfDefinition:
                if (!TryParse(key, out definition)) return false;
                _ = fields.GetFieldLayout(definition);
                return true;
            case UnitQueryKind.StaticField:
                if (!TryField(key, arguments, out field)) return false;
                _ = statics.GetStaticFieldLayout(field);
                return true;
            case UnitQueryKind.StaticFieldOfDefinition:
                if (!TryParse(key, out definition)) return false;
                _ = statics.GetStaticFieldLayout(definition);
                return true;
            case UnitQueryKind.StringLayout:
                _ = data.GetStringLayout(key);
                return true;
            case UnitQueryKind.StaticDataEnd:
                _ = data.StaticDataEnd;
                return true;
            case UnitQueryKind.StaticRootAddresses:
                _ = data.StaticRootAddresses;
                return true;
            case UnitQueryKind.DataSegments:
                _ = data.DataSegments;
                return true;
            case UnitQueryKind.TypeFactsTable:
                _ = key == "address" ? data.TypeFactsTableAddress : data.TypeFactsTableCount;
                return true;
            case UnitQueryKind.TypeObjectAddress:
                _ = data.TypeObjectAddresses.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.TypeObjectTable:
                _ = key switch
                {
                    "address" => data.TypeObjectTableAddress,
                    "cache" => data.TypeObjectCacheAddress,
                    "count" => data.TypeObjectCount,
                    _ => data.TypeObjectSize,
                };
                return true;
            case UnitQueryKind.MethodDescriptorAddress:
                if (!arguments.Methods.TryGetValue(key, out var method)) return false;
                _ = descriptors.GetDescriptorAddress(method);
                return true;
            case UnitQueryKind.FieldDescriptorAddress:
                if (!TryField(key, arguments, out field)) return false;
                _ = descriptors.GetDescriptorAddress(field);
                return true;
            case UnitQueryKind.MemberDescriptorOffsets:
                _ = key == "declaring-type-id"
                    ? descriptors.DeclaringTypeIdOffset
                    : descriptors.RequiresDeclaringTypeOffset;
                return true;
            case UnitQueryKind.ExceptionObject:
                if (!Enum.TryParse<ManagedExceptionKind>(key, out var exception)) return false;
                _ = exceptions.GetExceptionObject(exception);
                return true;
            case UnitQueryKind.TypeDescriptors:
                _ = typeDescriptors.TypeDescriptors;
                return true;
            case UnitQueryKind.ConstructedTypeDescriptors:
                _ = typeDescriptors.ConstructedTypeDescriptors;
                return true;
            case UnitQueryKind.ValueTypeDescriptors:
                _ = typeDescriptors.ValueTypeDescriptors;
                return true;
            case UnitQueryKind.MetadataTypeDescriptors:
                _ = typeDescriptors.MetadataTypeDescriptors;
                return true;
            case UnitQueryKind.EnumMetadata:
                _ = ((IEnumMetadataSource)typeDescriptors).EnumMetadata;
                return true;
            case UnitQueryKind.ManagedCallSite:
            {
                var separator = key.LastIndexOf('@');
                if (separator < 0 || !int.TryParse(key.AsSpan(separator + 1), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var offset))
                {
                    return false;
                }
                _ = view.ManagedCallSites.TryGetValue(
                    new ManagedCallSiteKey(new ManagedMethodIdentity(key[..separator]), offset), out _);
                return true;
            }
            case UnitQueryKind.ByRefEffects:
                _ = view.ManagedByRefEffects.Methods.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.DispatchCallSite:
                _ = view.DispatchCallSites.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.TypeTestSite:
                _ = view.TypeTestSites.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.RootMap:
                _ = view.RootMaps.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.FunctionAddress:
            {
                var found = key.StartsWith("c:", StringComparison.Ordinal)
                    ? view.FunctionIndices.TryGetConstructedMethod(key[2..], out var index)
                    : TryParse(key[2..], out var function) & view.FunctionIndices.TryGetMethod(function, out index);
                recorder.Record(UnitQueryKind.FunctionAddress, key,
                    UnitTargetViews.FunctionAddressAnswer(found, found ? index.Value : -1));
                return true;
            }
            case UnitQueryKind.Finalizable:
                if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeId)) return false;
                _ = view.Finalization.IsFinalizable(typeId);
                return true;
            case UnitQueryKind.DelegateTypes:
                _ = view.DelegateTypes.Count;
                return true;
            case UnitQueryKind.CallableMethods:
                _ = view.CallableMethods.Count;
                return true;
            case UnitQueryKind.TargetPlans:
                recorder.Record(UnitQueryKind.TargetPlans, key, UnitTargetFingerprint.Of(view));
                return true;
            case UnitQueryKind.StackTraceDirectMethodId:
                if (!TryParse(key, out definition)) return false;
                _ = view.StackTraceMethods.DirectMethodIds.TryGetValue(definition, out _);
                return true;
            case UnitQueryKind.StackTraceConstructedMethodId:
                _ = view.StackTraceMethods.ConstructedMethodIds.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.StackTraceLocationSymbols:
                if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var methodId)) return false;
                _ = view.StackTraceMethods.LocationSymbols.TryGetValue(methodId, out _);
                return true;
            case UnitQueryKind.StaticInitializerGuard:
                _ = view.ModuleData.StaticInitializerGuards.TryGetValue(key, out _);
                return true;
            case UnitQueryKind.ExceptionMetadata:
                if (!UnitTargetViews.TryParseExceptionGroupKey(key, out var group)) return false;
                _ = view.ModuleData.ExceptionMetadata.TryGetValue(group, out _);
                return true;
            default:
                return false;
        }
    }

    // A type by canonical name: one the arguments name, the type of a field the method names
    // by definition, or an array of one, as emission builds arrays from their element types.
    private bool TryType(string key, UnitQueryArguments arguments, out CliTypeIdentity type) =>
        TryType(key, arguments, fieldDefinitions, out type);

    internal static bool TryType(string key, UnitQueryArguments arguments, IFieldRepository fieldDefinitions,
        out CliTypeIdentity type)
    {
        if (arguments.Types.TryGetValue(key, out type!))
            return true;
        foreach (var entity in arguments.Definitions)
        {
            if ((entity.MetadataToken >>> 24) != FieldTable)
                continue;
            var fieldType = fieldDefinitions.GetField(entity).SignatureType;
            if (string.Equals(fieldType.CanonicalName, key, StringComparison.Ordinal))
            {
                type = fieldType;
                return true;
            }
        }
        var open = key.LastIndexOf('[');
        if (open <= 0 || !key.EndsWith(']'))
            return false;
        if (!TryType(key[..open], arguments, fieldDefinitions, out var element))
            return false;
        var shape = key.AsSpan(open + 1, key.Length - open - 2);
        if (shape.IsEmpty)
        {
            type = CliTypeIdentity.SzArray(element);
            return true;
        }
        if (shape is "*")
        {
            type = CliTypeIdentity.Array(element, 1);
            return true;
        }
        if (shape.Trim(',').IsEmpty)
        {
            type = CliTypeIdentity.Array(element, shape.Length + 1);
            return true;
        }
        return false;
    }

    // A field by canonical name: one the arguments name, or a field of a type they name,
    // instantiated as emission instantiates a declaring type's fields.
    private bool TryField(string key, UnitQueryArguments arguments, out FieldInstanceModel field)
    {
        if (arguments.Fields.TryGetValue(key, out field!))
            return true;
        var separator = key.LastIndexOf("::0x", StringComparison.Ordinal);
        if (separator <= 0 || !int.TryParse(key.AsSpan(separator + 4), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var token) ||
            !TryType(key[..separator], arguments, out var declaring))
        {
            return false;
        }
        foreach (var candidate in typeDefinitions.ResolveTypeIdentity(declaring).Fields)
        {
            if (candidate.MetadataToken != token)
                continue;
            var definition = fieldDefinitions.GetField(candidate);
            field = new(definition, declaring, definition.SignatureType.Substitute(declaring.TypeArguments));
            return string.Equals(field.CanonicalName, key, StringComparison.Ordinal);
        }
        return false;
    }

    internal static bool TryParse(string key, out EntityKey definition)
    {
        definition = default;
        var separator = key.LastIndexOf(":0x", StringComparison.Ordinal);
        if (separator < 0 || !int.TryParse(key.AsSpan(separator + 3), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var token))
        {
            return false;
        }
        definition = new(new AssemblyIdentity(key[..separator]), token);
        return true;
    }
}

/// <summary>The types, fields and methods a method's body names, by canonical name.</summary>
internal sealed record UnitQueryArguments(
    IReadOnlyDictionary<string, CliTypeIdentity> Types,
    IReadOnlyDictionary<string, FieldInstanceModel> Fields,
    IReadOnlyDictionary<string, MethodInstanceModel> Methods)
{
    // The members one method's body names by definition (non-generic owners), whose types
    // are found through the metadata only when a query needs them.
    public ImmutableArray<EntityKey> Definitions { get; init; } = [];

    // This method's identities, then the program's for names it does not hold.
    public UnitQueryArguments Before(UnitQueryArguments program) => new(
        new Fallback<CliTypeIdentity>(Types, program.Types),
        new Fallback<FieldInstanceModel>(Fields, program.Fields),
        new Fallback<MethodInstanceModel>(Methods, program.Methods))
    {
        Definitions = Definitions,
    };

    // A method's identities from its analysed body.
    public static UnitQueryArguments Of(NetWasm.Compiler.ControlFlow.ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var definitions = ImmutableArray.CreateBuilder<EntityKey>();
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is CilOperand.Entity entity) definitions.Add(entity.Key);
        }
        return Of(method.Method, method.Body.Method, method.Body.LocalSignatureTypes, method.Body.Instructions,
            method.Body.ExceptionRegions.Select(region => region.CatchTypeIdentity)) with
        {
            Definitions = definitions.ToImmutable(),
        };
    }

    // Every identity the analysed bodies name.
    public static UnitQueryArguments Of(IEnumerable<NetWasm.Compiler.ControlFlow.ManagedMethodBody> all)
    {
        var index = new Index();
        foreach (var method in all)
        {
            index.AddBody(method.Method, method.Body.Method, method.Body.LocalSignatureTypes, method.Body.Instructions,
                method.Body.ExceptionRegions.Select(region => region.CatchTypeIdentity));
        }
        return index.ToArguments();
    }

    private static UnitQueryArguments Of(
        MethodInstanceModel? instance,
        MethodDefinitionModel definition,
        ImmutableArray<CliTypeIdentity> locals,
        ImmutableArray<CilInstruction> instructions,
        IEnumerable<CliTypeIdentity?> catchTypes)
    {
        var index = new Index();
        index.AddBody(instance, definition, locals, instructions, catchTypes);
        return index.ToArguments();
    }

    // The identities bodies name, transitively through type arguments, element types,
    // signatures and field types.
    private sealed class Index
    {
        private readonly Dictionary<string, CliTypeIdentity> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FieldInstanceModel> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MethodInstanceModel> _methods = new(StringComparer.Ordinal);

        internal UnitQueryArguments ToArguments() => new(_types, _fields, _methods);

        internal void AddBody(
            MethodInstanceModel? instance,
            MethodDefinitionModel definition,
            ImmutableArray<CliTypeIdentity> locals,
            ImmutableArray<CilInstruction> instructions,
            IEnumerable<CliTypeIdentity?> catchTypes)
        {
            if (instance is not null) AddMethod(instance);
            AddType(definition.Signature.ReturnSignatureType);
            foreach (var parameter in definition.Signature.ParameterSignatureTypes) AddType(parameter);
            foreach (var local in locals) AddType(local);
            foreach (var instruction in instructions)
            {
                switch (instruction.Operand)
                {
                    case CilOperand.MethodInstance call: AddMethod(call.Value); break;
                    case CilOperand.FieldInstance access: AddField(access.Value); break;
                    case CilOperand.TypeIdentity type: AddType(type.Value); break;
                    case CilOperand.ExactType type: AddType(type.Value); break;
                    case CilOperand.ArrayInitializerData initializer: AddField(initializer.Field); break;
                    case CilOperand.ReadOnlySpanData span:
                        AddField(span.Field);
                        AddType(span.SpanType);
                        AddType(span.ElementType);
                        break;
                    case CilOperand.CallSite site:
                        AddType(site.Signature.ReturnSignatureType);
                        foreach (var parameter in site.Signature.ParameterSignatureTypes) AddType(parameter);
                        break;
                }
            }
            foreach (var catchType in catchTypes) AddType(catchType);
        }

        private void AddType(CliTypeIdentity? type)
        {
            if (type is null || !_types.TryAdd(type.CanonicalName, type)) return;
            foreach (var argument in type.TypeArguments) AddType(argument);
            AddType(type.ElementType);
        }

        private void AddMethod(MethodInstanceModel instance)
        {
            if (!_methods.TryAdd(instance.CanonicalName, instance)) return;
            AddType(instance.DeclaringType);
            foreach (var argument in instance.MethodArguments) AddType(argument);
            AddType(instance.Signature.ReturnSignatureType);
            foreach (var parameter in instance.Signature.ParameterSignatureTypes) AddType(parameter);
        }

        private void AddField(FieldInstanceModel field)
        {
            if (!_fields.TryAdd(field.CanonicalName, field)) return;
            AddType(field.DeclaringType);
            AddType(field.FieldType);
        }
    }

    public static UnitQueryArguments Of(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var definitions = ImmutableArray.CreateBuilder<EntityKey>();
        foreach (var instruction in method.Header.Instructions)
        {
            if (instruction.Operand is CilOperand.Entity entity) definitions.Add(entity.Key);
        }
        return Of([method]) with { Definitions = definitions.ToImmutable() };
    }

    // Every identity the given methods' bodies name, transitively through type arguments,
    // element types, signatures and field types.
    public static UnitQueryArguments Of(IEnumerable<StructuredMethod> all)
    {
        var types = new Dictionary<string, CliTypeIdentity>(StringComparer.Ordinal);
        var fields = new Dictionary<string, FieldInstanceModel>(StringComparer.Ordinal);
        var methods = new Dictionary<string, MethodInstanceModel>(StringComparer.Ordinal);
        void AddType(CliTypeIdentity? type)
        {
            if (type is null || !types.TryAdd(type.CanonicalName, type)) return;
            foreach (var argument in type.TypeArguments) AddType(argument);
            AddType(type.ElementType);
        }
        void AddMethod(MethodInstanceModel instance)
        {
            if (!methods.TryAdd(instance.CanonicalName, instance)) return;
            AddType(instance.DeclaringType);
            foreach (var argument in instance.MethodArguments) AddType(argument);
            AddType(instance.Signature.ReturnSignatureType);
            foreach (var parameter in instance.Signature.ParameterSignatureTypes) AddType(parameter);
        }
        void AddField(FieldInstanceModel field)
        {
            if (!fields.TryAdd(field.CanonicalName, field)) return;
            AddType(field.DeclaringType);
            AddType(field.FieldType);
        }
        foreach (var method in all)
        {
        var header = method.Header;
        if (header.MethodInstance is { } self) AddMethod(self);
        AddType(header.Method.Signature.ReturnSignatureType);
        foreach (var parameter in header.Method.Signature.ParameterSignatureTypes) AddType(parameter);
        foreach (var local in header.LocalSignatureTypes) AddType(local);
        foreach (var instruction in header.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.MethodInstance call: AddMethod(call.Value); break;
                case CilOperand.FieldInstance access: AddField(access.Value); break;
                case CilOperand.TypeIdentity type: AddType(type.Value); break;
                case CilOperand.ExactType type: AddType(type.Value); break;
                case CilOperand.ArrayInitializerData initializer: AddField(initializer.Field); break;
                case CilOperand.ReadOnlySpanData span:
                    AddField(span.Field);
                    AddType(span.SpanType);
                    AddType(span.ElementType);
                    break;
                case CilOperand.CallSite site:
                    AddType(site.Signature.ReturnSignatureType);
                    foreach (var parameter in site.Signature.ParameterSignatureTypes) AddType(parameter);
                    break;
            }
        }
        foreach (var group in method.ExceptionGroups.Values)
        {
            foreach (var clause in group.Clauses) AddType(clause.CatchTypeIdentity);
        }
        }
        return new(types, fields, methods);
    }

    private sealed class Fallback<T>(
        IReadOnlyDictionary<string, T> first,
        IReadOnlyDictionary<string, T> second) : IReadOnlyDictionary<string, T>
    {
        public T this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);
        public IEnumerable<string> Keys => first.Keys.Concat(second.Keys);
        public IEnumerable<T> Values => first.Values.Concat(second.Values);
        public int Count => first.Count + second.Count;
        public bool ContainsKey(string key) => first.ContainsKey(key) || second.ContainsKey(key);
        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) =>
            first.TryGetValue(key, out value) || second.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => first.Concat(second).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
