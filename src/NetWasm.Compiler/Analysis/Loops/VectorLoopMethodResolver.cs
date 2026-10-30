using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Loops;

// A method a vectorized loop calls: the type that declares it, its name, which
// overload it is and how a lane or element type instantiates it.
internal abstract record VectorLoopMethod(string Type, string Name)
{
    public static VectorLoopMethod ArrayData { get; } = new GenericMethod(
        "System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference", 1);

    // Vector128.LoadUnsafe<T>(ref readonly T, nuint) and
    // StoreUnsafe<T>(Vector128<T>, ref T, nuint).
    public static VectorLoopMethod Load { get; } = new GenericMethod("System.Runtime.Intrinsics.Vector128", "LoadUnsafe", 2);

    public static VectorLoopMethod Store { get; } = new GenericMethod("System.Runtime.Intrinsics.Vector128", "StoreUnsafe", 3);

    // Vector128.Create<T>(T): every lane the value.
    public static VectorLoopMethod Broadcast { get; } = new BroadcastMethod();

    // Vector128.Create(int, int, int, int): lanes 0, 1, 2 and 3 from the
    // arguments, whatever the lane type.
    public static VectorLoopMethod CreateInt32 { get; } = new CreateInt32Method();

    public abstract bool Matches(MethodDefinitionModel method);

    public abstract CliGenericContext Context(CliTypeIdentity lane);

    // A generic method with Parameters parameters, instantiated for the lane.
    private sealed record GenericMethod(string Type, string Name, int Parameters) : VectorLoopMethod(Type, Name)
    {
        public override bool Matches(MethodDefinitionModel method) =>
            method.GenericArity == 1 && method.Signature.ParameterSignatureTypes.Length == Parameters;

        public override CliGenericContext Context(CliTypeIdentity lane) => new([], [lane]);
    }

    private sealed record BroadcastMethod() : VectorLoopMethod("System.Runtime.Intrinsics.Vector128", "Create")
    {
        public override bool Matches(MethodDefinitionModel method) =>
            method.GenericArity == 1 &&
            method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericMethodParameter }];

        public override CliGenericContext Context(CliTypeIdentity lane) => new([], [lane]);
    }

    private sealed record CreateInt32Method() : VectorLoopMethod("System.Runtime.Intrinsics.Vector128", "Create")
    {
        public override bool Matches(MethodDefinitionModel method) =>
            method.GenericArity == 0 &&
            method.Signature.ParameterSignatureTypes is { Length: 4 } parameters &&
            parameters.All(static parameter => parameter.CanonicalName == "primitive:i4");

        public override CliGenericContext Context(CliTypeIdentity lane) => new([], []);
    }
}

// A Vector128<T> operator by name, op_Addition or op_LeftShift for instance:
// binary operators take two vectors and shifts a vector and an int.
internal sealed record VectorLoopOperator(string Name) : VectorLoopMethod("System.Runtime.Intrinsics.Vector128`1", Name)
{
    public override bool Matches(MethodDefinitionModel method) =>
        method.IsStatic &&
        (Name is "op_UnaryNegation" or "op_OnesComplement"
            ? method.Signature.ParameterSignatureTypes.Length == 1
            : Name is "op_LeftShift" or "op_RightShift" or "op_UnsignedRightShift"
                ? method.Signature.ParameterSignatureTypes is [_, { CanonicalName: "primitive:i4" }]
                : method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericInstantiation }, { Shape: CliTypeShape.GenericInstantiation }]);

    public override CliGenericContext Context(CliTypeIdentity lane) => new([lane], []);
}

internal interface IVectorLoopMethodResolver
{
    MethodInstanceModel Resolve(VectorLoopMethod method, CliTypeIdentity lane);
}

internal sealed class VectorLoopMethodResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IVectorLoopMethodResolver
{
    private readonly ConcurrentDictionary<(VectorLoopMethod Method, string Lane), MethodInstanceModel> _instances = new();

    public MethodInstanceModel Resolve(VectorLoopMethod method, CliTypeIdentity lane) =>
        _instances.GetOrAdd((method, lane.CanonicalName), _ =>
        {
            var definition = types.FindType(method.Type).Methods.Select(methods.GetMethod)
                .Single(candidate => candidate.Name == method.Name && method.Matches(candidate));
            return instances.ResolveMethodInstance(definition.Key.Assembly, definition.Key.MetadataToken,
                symbols.Format(definition), 0, method.Context(lane));
        });
}
