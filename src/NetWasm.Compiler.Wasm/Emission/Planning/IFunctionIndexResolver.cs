using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IFunctionIndexResolver
{
    // The module's shared function that begins and throws a managed exception.
    OptionalFunctionIndex ManagedThrowFunction { get; }

    int Resolve(EntityKey method);

    int Resolve(string method);

    int Resolve(MethodInstanceModel method);

    int Resolve(InteropImportKey import);

    // A function's index written as a value (a function pointer), not as a call operand.
    // Compiled units rebind call operands by symbol but cannot rebind a value, so a unit
    // depends on the index itself.
    int Resolve(FunctionAddress address) => Resolve(address.Method);
}

/// <summary>A method whose function index emission writes as a value.</summary>
internal readonly record struct FunctionAddress(MethodInstanceModel Method);
