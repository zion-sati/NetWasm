using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IVectorLoopMethodResolver
{
    // MemoryMarshal.GetArrayDataReference<T>(T[]).
    MethodInstanceModel ArrayData(CliTypeIdentity element);

    // Vector128.LoadUnsafe<T>(ref readonly T, nuint) and
    // StoreUnsafe<T>(Vector128<T>, ref T, nuint).
    MethodInstanceModel Load(CliTypeIdentity lane);

    MethodInstanceModel Store(CliTypeIdentity lane);

    // Vector128.Create<T>(T): every lane the value.
    MethodInstanceModel Broadcast(CliTypeIdentity lane);

    // Vector128.Create(int, int, int, int): lanes 0, 1, 2 and 3 from the arguments.
    MethodInstanceModel CreateInt32();

    // A Vector128<T> operator by name, op_Addition or op_LeftShift for
    // instance; binary operators take two vectors and shifts a vector and an int.
    MethodInstanceModel Operator(string name, CliTypeIdentity lane);
}

internal sealed class VectorLoopMethodResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IVectorLoopMethodResolver
{
    private readonly ConcurrentDictionary<(string Name, string Lane), MethodInstanceModel> _instances = new();

    public MethodInstanceModel ArrayData(CliTypeIdentity element) =>
        Generic("System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference", element,
            static method => method.GenericArity == 1);

    public MethodInstanceModel Load(CliTypeIdentity lane) =>
        Generic("System.Runtime.Intrinsics.Vector128", "LoadUnsafe", lane,
            static method => method.Signature.ParameterSignatureTypes.Length == 2);

    public MethodInstanceModel Store(CliTypeIdentity lane) =>
        Generic("System.Runtime.Intrinsics.Vector128", "StoreUnsafe", lane,
            static method => method.Signature.ParameterSignatureTypes.Length == 3);

    public MethodInstanceModel Broadcast(CliTypeIdentity lane) =>
        Generic("System.Runtime.Intrinsics.Vector128", "Create", lane,
            static method => method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericMethodParameter }]);

    public MethodInstanceModel CreateInt32() =>
        _instances.GetOrAdd(("Create4", "primitive:i4"), _ =>
        {
            var method = Find("System.Runtime.Intrinsics.Vector128", "Create", static method =>
                method.GenericArity == 0 &&
                method.Signature.ParameterSignatureTypes is { Length: 4 } parameters &&
                parameters.All(static parameter => parameter.CanonicalName == "primitive:i4"));
            return Instantiate(method, new CliGenericContext([], []));
        });

    public MethodInstanceModel Operator(string name, CliTypeIdentity lane) =>
        _instances.GetOrAdd((name, lane.CanonicalName), _ =>
        {
            var method = Find("System.Runtime.Intrinsics.Vector128`1", name, method =>
                method.IsStatic &&
                (name is "op_UnaryNegation" or "op_OnesComplement"
                    ? method.Signature.ParameterSignatureTypes.Length == 1
                    : name is "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift"
                        ? method.Signature.ParameterSignatureTypes is [_, { CanonicalName: "primitive:i4" }]
                        : method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericInstantiation }, { Shape: CliTypeShape.GenericInstantiation }]));
            return Instantiate(method, new CliGenericContext([lane], []));
        });

    private MethodInstanceModel Generic(
        string type,
        string name,
        CliTypeIdentity argument,
        Func<MethodDefinitionModel, bool> select) =>
        _instances.GetOrAdd((type + "." + name, argument.CanonicalName), _ =>
            Instantiate(Find(type, name, method => method.GenericArity == 1 && select(method)),
                new CliGenericContext([], [argument])));

    private MethodDefinitionModel Find(string type, string name, Func<MethodDefinitionModel, bool> select) =>
        types.FindType(type).Methods.Select(methods.GetMethod).Single(method => method.Name == name && select(method));

    private MethodInstanceModel Instantiate(MethodDefinitionModel method, CliGenericContext context) =>
        instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0, context);
}
